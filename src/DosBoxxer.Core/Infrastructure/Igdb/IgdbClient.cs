using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Infrastructure.Igdb.Dto;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Igdb;

public sealed class IgdbCallResult
{
    private IgdbCallResult(IReadOnlyList<IgdbGame>? games, MetadataErrorKind error, string? detail)
    {
        Games = games;
        Error = error;
        Detail = detail;
    }

    public IReadOnlyList<IgdbGame>? Games { get; }

    public MetadataErrorKind Error { get; }

    public string? Detail { get; }

    public bool IsSuccess => Error == MetadataErrorKind.None && Games is not null;

    public static IgdbCallResult Success(IReadOnlyList<IgdbGame> games) => new(games, MetadataErrorKind.None, null);

    public static IgdbCallResult Failure(MetadataErrorKind error, string? detail = null) => new(null, error, detail);
}

/// <summary>
/// Low level access to the IGDB API v4.
///
/// Authentication uses the Twitch OAuth2 client-credentials flow: the client id and secret are
/// exchanged at <c>https://id.twitch.tv/oauth2/token</c> for a bearer token that is cached in
/// memory until shortly before it expires. Every IGDB request then carries the
/// <c>Client-ID</c> and <c>Authorization: Bearer</c> headers.
///
/// Requests use the Apicalypse query language in the POST body. The documented rate limit is
/// four requests per second, which this client enforces with a minimum interval. Images are
/// served from <c>images.igdb.com</c>.
/// </summary>
public sealed class IgdbClient : IMediaHttpClient, IDisposable
{
    public const string ApiHttpClientName = "igdb";
    public const string TokenHttpClientName = "igdb-token";
    public const string ApiBaseAddress = "https://api.igdb.com/v4/";
    public const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    public const string ImageBaseAddress = "https://images.igdb.com/igdb/image/upload/";

    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(300);
    private const int MaxAttempts = 3;
    private const long MaxMediaBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IgdbClient> _logger;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private string? _cachedToken;
    private string? _cachedTokenClientId;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public IgdbClient(IHttpClientFactory httpClientFactory, ILogger<IgdbClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Builds the field/filter clauses shared by search and by-id queries. The dotted paths make
    /// IGDB expand the nested cover, screenshots, genres and companies in a single request.
    /// </summary>
    private const string Fields =
        "fields name,summary,first_release_date,cover.image_id,screenshots.image_id," +
        "genres.name,platforms.name,involved_companies.company.name," +
        "involved_companies.developer,involved_companies.publisher;";

    public Task<IgdbCallResult> SearchAsync(
        IgdbCredentials credentials,
        string title,
        int platformId,
        CancellationToken cancellationToken)
    {
        // Apicalypse: search + platform filter. The search term is quoted and any embedded quote
        // is stripped so the query cannot be broken out of.
        var safeTitle = title.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
        var body = $"search \"{safeTitle}\"; {Fields} where platforms = ({platformId}); limit 20;";
        return SendAsync(credentials, body, cancellationToken);
    }

    public Task<IgdbCallResult> GetGameAsync(IgdbCredentials credentials, long gameId, CancellationToken cancellationToken)
    {
        var body = $"{Fields} where id = {gameId}; limit 1;";
        return SendAsync(credentials, body, cancellationToken);
    }

    private async Task<IgdbCallResult> SendAsync(IgdbCredentials credentials, string body, CancellationToken cancellationToken)
    {
        if (!credentials.IsUsable)
        {
            return IgdbCallResult.Failure(MetadataErrorKind.NotConfigured);
        }

        var token = await GetTokenAsync(credentials, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return IgdbCallResult.Failure(MetadataErrorKind.InvalidCredentials);
        }

        var client = _httpClientFactory.CreateClient(ApiHttpClientName);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ThrottleAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("IGDB request (attempt {Attempt}/{Max})", attempt, MaxAttempts);

            using var request = new HttpRequestMessage(HttpMethod.Post, "games")
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain"),
            };
            request.Headers.TryAddWithoutValidation("Client-ID", credentials.ClientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            string payload;

            try
            {
                response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("IGDB request failed: {Message}", LogSanitizer.Sanitize(ex.Message));
                if (attempt == MaxAttempts)
                {
                    return IgdbCallResult.Failure(MetadataErrorKind.NetworkUnavailable, LogSanitizer.Sanitize(ex.Message));
                }

                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == MaxAttempts)
                {
                    return IgdbCallResult.Failure(MetadataErrorKind.ProviderUnavailable, "timeout");
                }

                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("IGDB rate limit reached (HTTP 429)");
                    if (attempt == MaxAttempts)
                    {
                        return IgdbCallResult.Failure(MetadataErrorKind.RateLimited);
                    }

                    await BackoffAsync(attempt, cancellationToken, response.Headers.RetryAfter?.Delta).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // The token may have been revoked; drop it so the next call re-authenticates.
                    InvalidateToken();
                    return IgdbCallResult.Failure(MetadataErrorKind.InvalidCredentials);
                }

                if ((int)response.StatusCode >= 500)
                {
                    if (attempt == MaxAttempts)
                    {
                        return IgdbCallResult.Failure(MetadataErrorKind.ProviderUnavailable);
                    }

                    await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return IgdbCallResult.Failure(MetadataErrorKind.InvalidResponse, Truncate(payload));
                }
            }

            try
            {
                var games = JsonSerializer.Deserialize<List<IgdbGame>>(payload, JsonOptions) ?? new List<IgdbGame>();
                return games.Count == 0
                    ? IgdbCallResult.Failure(MetadataErrorKind.NoResults)
                    : IgdbCallResult.Success(games);
            }
            catch (JsonException ex)
            {
                _logger.LogError("IGDB response could not be parsed: {Message}", LogSanitizer.Sanitize(ex.Message));
                return IgdbCallResult.Failure(MetadataErrorKind.InvalidResponse, ex.Message);
            }
        }

        return IgdbCallResult.Failure(MetadataErrorKind.ProviderUnavailable);
    }

    // ---- OAuth token ----------------------------------------------------------------------

    private async Task<string?> GetTokenAsync(IgdbCredentials credentials, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedToken is not null &&
                string.Equals(_cachedTokenClientId, credentials.ClientId, StringComparison.Ordinal) &&
                DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _cachedToken;
            }

            var client = _httpClientFactory.CreateClient(TokenHttpClientName);
            var url = TokenEndpoint +
                      "?client_id=" + Uri.EscapeDataString(credentials.ClientId) +
                      "&client_secret=" + Uri.EscapeDataString(credentials.ClientSecret) +
                      "&grant_type=client_credentials";

            using var response = await client.PostAsync(url, content: null, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("IGDB token request failed (HTTP {Status})", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var token = JsonSerializer.Deserialize<IgdbTokenResponse>(payload, JsonOptions);

            if (token?.AccessToken is null)
            {
                return null;
            }

            _cachedToken = token.AccessToken;
            _cachedTokenClientId = credentials.ClientId;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60));
            return _cachedToken;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("IGDB token could not be obtained ({Type})", ex.GetType().Name);
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _cachedToken = null;
        _tokenExpiry = DateTimeOffset.MinValue;
    }

    // ---- media ----------------------------------------------------------------------------

    /// <summary>Builds the CDN URL for an IGDB image id at the given size (e.g. <c>t_cover_big</c>).</summary>
    public static string BuildImageUrl(string imageId, string size) =>
        ImageBaseAddress + size + "/" + imageId + ".jpg";

    public bool AcceptsUrl(string url) => TryValidateMediaUrl(url, out _);

    public async Task<Stream?> DownloadMediaAsync(string url, CancellationToken cancellationToken)
    {
        if (!TryValidateMediaUrl(url, out var uri))
        {
            _logger.LogWarning("Refusing to download media from an unexpected host");
            return null;
        }

        var client = _httpClientFactory.CreateClient(ApiHttpClientName);

        using var response = await client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Media download failed with status {Status}", (int)response.StatusCode);
            return null;
        }

        var buffer = new MemoryStream();
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        if (buffer.Length > MaxMediaBytes)
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        buffer.Position = 0;
        return buffer;
    }

    internal static bool TryValidateMediaUrl(string? url, out Uri uri)
    {
        uri = null!;

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        var host = parsed.Host;
        var allowed = host.Equals("images.igdb.com", StringComparison.OrdinalIgnoreCase) ||
                      host.EndsWith(".igdb.com", StringComparison.OrdinalIgnoreCase);

        if (!allowed)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequest;
            if (elapsed < MinimumRequestInterval)
            {
                await Task.Delay(MinimumRequestInterval - elapsed, cancellationToken).ConfigureAwait(false);
            }

            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static Task BackoffAsync(int attempt, CancellationToken cancellationToken, TimeSpan? retryAfter = null)
    {
        var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
        if (delay > TimeSpan.FromSeconds(30))
        {
            delay = TimeSpan.FromSeconds(30);
        }

        return Task.Delay(delay, cancellationToken);
    }

    private static string Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= 300 ? value : value[..300];

    public void Dispose()
    {
        _throttle.Dispose();
        _tokenLock.Dispose();
    }
}

/// <summary>Credential bundle for IGDB. Never logged.</summary>
public sealed class IgdbCredentials
{
    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
