using System.Globalization;
using System.Net;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Infrastructure.MobyGames.Dto;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.MobyGames;

public sealed class MobyGamesCallResult<T>
    where T : class
{
    private MobyGamesCallResult(T? value, MetadataErrorKind error, string? detail)
    {
        Value = value;
        Error = error;
        Detail = detail;
    }

    public T? Value { get; }

    public MetadataErrorKind Error { get; }

    public string? Detail { get; }

    public bool IsSuccess => Error == MetadataErrorKind.None && Value is not null;

    public static MobyGamesCallResult<T> Success(T value) => new(value, MetadataErrorKind.None, null);

    public static MobyGamesCallResult<T> Failure(MetadataErrorKind error, string? detail = null) =>
        new(null, error, detail);
}

/// <summary>
/// Low level HTTP access to the MobyGames API v1 (<c>https://api.mobygames.com/v1/</c>).
///
/// Endpoints used (documented at <c>https://www.mobygames.com/info/api/</c>):
/// <list type="bullet">
/// <item><c>games</c> — search by <c>title</c>, filtered to a <c>platform</c>, <c>format=normal</c>.</item>
/// <item><c>games/{id}/platforms/{platform}</c> — release details (companies, players, date).</item>
/// </list>
///
/// Authentication is the private <c>api_key</c> query argument, obtained on request from
/// MobyGames. The documented limit is 360 requests/hour — one every ten seconds, and never more
/// than one per second — which this client enforces with a minimum request interval.
/// </summary>
public sealed class MobyGamesClient : IMediaHttpClient, IDisposable
{
    public const string HttpClientName = "mobygames";
    public const string BaseAddress = "https://api.mobygames.com/v1/";

    /// <summary>MobyGames documents one request every ten seconds; stay just above that.</summary>
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(10_500);
    private const int MaxAttempts = 3;
    private const long MaxMediaBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MobyGamesClient> _logger;
    private readonly SemaphoreSlim _throttle = new(1, 1);

    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public MobyGamesClient(IHttpClientFactory httpClientFactory, ILogger<MobyGamesClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<MobyGamesCallResult<MobyGamesGamesResponse>> SearchAsync(
        string apiKey,
        string title,
        int platformId,
        CancellationToken cancellationToken)
    {
        var query = new List<(string, string)>
        {
            ("format", "normal"),
            ("platform", platformId.ToString(CultureInfo.InvariantCulture)),
            ("title", title),
            ("limit", "20"),
        };

        return GetAsync<MobyGamesGamesResponse>("games", apiKey, query, cancellationToken);
    }

    /// <summary>Fetches a single game by id in <c>normal</c> format (cover, screenshots, genres).</summary>
    public Task<MobyGamesCallResult<MobyGamesGamesResponse>> GetGameAsync(
        string apiKey,
        long gameId,
        CancellationToken cancellationToken)
    {
        var query = new List<(string, string)>
        {
            ("format", "normal"),
            ("id", gameId.ToString(CultureInfo.InvariantCulture)),
        };

        return GetAsync<MobyGamesGamesResponse>("games", apiKey, query, cancellationToken);
    }

    public Task<MobyGamesCallResult<MobyPlatformDetail>> GetPlatformDetailAsync(
        string apiKey,
        long gameId,
        int platformId,
        CancellationToken cancellationToken)
    {
        var endpoint = $"games/{gameId.ToString(CultureInfo.InvariantCulture)}/platforms/{platformId.ToString(CultureInfo.InvariantCulture)}";
        return GetAsync<MobyPlatformDetail>(endpoint, apiKey, new List<(string, string)>(), cancellationToken);
    }

    private async Task<MobyGamesCallResult<T>> GetAsync<T>(
        string endpoint,
        string apiKey,
        IReadOnlyList<(string Key, string Value)> parameters,
        CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return MobyGamesCallResult<T>.Failure(MetadataErrorKind.NotConfigured);
        }

        var query = BuildQuery(apiKey, parameters);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ThrottleAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("MobyGames request {Endpoint} (attempt {Attempt}/{Max})", endpoint, attempt, MaxAttempts);

            HttpResponseMessage response;
            string body;

            try
            {
                response = await client.GetAsync(endpoint + "?" + query, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("MobyGames request failed: {Message}", LogSanitizer.Sanitize(ex.Message));
                if (attempt == MaxAttempts)
                {
                    return MobyGamesCallResult<T>.Failure(MetadataErrorKind.NetworkUnavailable, LogSanitizer.Sanitize(ex.Message));
                }

                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("MobyGames request timed out");
                if (attempt == MaxAttempts)
                {
                    return MobyGamesCallResult<T>.Failure(MetadataErrorKind.ProviderUnavailable, "timeout");
                }

                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("MobyGames rate limit reached (HTTP 429)");
                    if (attempt == MaxAttempts)
                    {
                        return MobyGamesCallResult<T>.Failure(MetadataErrorKind.RateLimited);
                    }

                    await BackoffAsync(attempt, cancellationToken, response.Headers.RetryAfter?.Delta).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("MobyGames rejected the API key (HTTP {Status})", (int)response.StatusCode);
                    return MobyGamesCallResult<T>.Failure(MetadataErrorKind.InvalidCredentials);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return MobyGamesCallResult<T>.Failure(MetadataErrorKind.NoResults);
                }

                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("MobyGames server error (HTTP {Status})", (int)response.StatusCode);
                    if (attempt == MaxAttempts)
                    {
                        return MobyGamesCallResult<T>.Failure(MetadataErrorKind.ProviderUnavailable);
                    }

                    await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = TryParseError(body);
                    _logger.LogWarning("MobyGames returned HTTP {Status}: {Error}", (int)response.StatusCode, error);
                    return MobyGamesCallResult<T>.Failure(MetadataErrorKind.InvalidResponse, error);
                }
            }

            try
            {
                var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
                return value is null
                    ? MobyGamesCallResult<T>.Failure(MetadataErrorKind.InvalidResponse)
                    : MobyGamesCallResult<T>.Success(value);
            }
            catch (JsonException ex)
            {
                _logger.LogError("MobyGames response could not be parsed: {Message}", LogSanitizer.Sanitize(ex.Message));
                return MobyGamesCallResult<T>.Failure(MetadataErrorKind.InvalidResponse, ex.Message);
            }
        }

        return MobyGamesCallResult<T>.Failure(MetadataErrorKind.ProviderUnavailable);
    }

    public bool AcceptsUrl(string url) => TryValidateMediaUrl(url, out _);

    public async Task<Stream?> DownloadMediaAsync(string url, CancellationToken cancellationToken)
    {
        if (!TryValidateMediaUrl(url, out var uri))
        {
            _logger.LogWarning("Refusing to download media from an unexpected host");
            return null;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);

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
            _logger.LogWarning("Refusing to store a media file larger than {Limit} bytes", MaxMediaBytes);
            await buffer.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// Accepts only absolute http(s) URLs on a mobygames.com host, so a manipulated API response
    /// cannot make the launcher fetch arbitrary hosts. Both the legacy
    /// <c>www.mobygames.com/images/…</c> and the current <c>cdn.mobygames.com/…</c> forms pass.
    /// </summary>
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
        var allowed = host.Equals("mobygames.com", StringComparison.OrdinalIgnoreCase) ||
                      host.EndsWith(".mobygames.com", StringComparison.OrdinalIgnoreCase);

        if (!allowed)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static string BuildQuery(string apiKey, IReadOnlyList<(string Key, string Value)> parameters)
    {
        var pairs = new List<string>(parameters.Count + 1)
        {
            "api_key=" + Uri.EscapeDataString(apiKey),
        };

        foreach (var (key, value) in parameters)
        {
            pairs.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
        }

        return string.Join('&', pairs);
    }

    private static string? TryParseError(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<MobyError>(body, JsonOptions);
            return error?.Message ?? error?.Error;
        }
        catch (JsonException)
        {
            return body.Length <= 200 ? body : body[..200];
        }
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
        var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt) * 5);
        if (delay > TimeSpan.FromSeconds(60))
        {
            delay = TimeSpan.FromSeconds(60);
        }

        return Task.Delay(delay, cancellationToken);
    }

    public void Dispose() => _throttle.Dispose();
}
