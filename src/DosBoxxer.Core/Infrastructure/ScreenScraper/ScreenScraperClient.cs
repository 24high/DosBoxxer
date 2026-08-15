using System.Globalization;
using System.Net;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Infrastructure.ScreenScraper.Dto;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.ScreenScraper;

public sealed class ScreenScraperCallResult
{
    public required MetadataErrorKind Error { get; init; }

    public SsEnvelope? Envelope { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Error == MetadataErrorKind.None && Envelope is not null;
}

/// <summary>
/// Low level HTTP access to the ScreenScraper API v2.
///
/// Endpoints and parameters follow the official API v2 description published at
/// <c>https://api.screenscraper.fr/webapi2.php</c>:
/// <list type="bullet">
/// <item><c>ssuserInfos.php</c> — account status, used to validate the credentials.</item>
/// <item><c>jeuRecherche.php</c> — game search by name (<c>recherche</c>).</item>
/// <item><c>jeuInfos.php</c> — full record for one game (<c>gameid</c>).</item>
/// </list>
/// Common parameters: <c>devid</c>, <c>devpassword</c>, <c>softname</c>, <c>output=json</c> and
/// the optional end-user account <c>ssid</c> / <c>sspassword</c>.
///
/// NOTE ON REGISTRATION: <c>devid</c>/<c>devpassword</c> and the registered <c>softname</c> are
/// issued by ScreenScraper to the application author on request; they are entered in the
/// settings dialog and are never compiled into this source. Anonymous access (no user account)
/// is permitted but heavily throttled by the service.
///
/// The class enforces a minimum interval between requests, retries only on transient failures
/// with exponential backoff, and honours HTTP 429.
/// </summary>
public sealed class ScreenScraperClient : IMediaHttpClient, IDisposable
{
    public const string HttpClientName = "screenscraper";
    public const string BaseAddress = "https://api.screenscraper.fr/api2/";

    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1200);
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScreenScraperClient> _logger;
    private readonly SemaphoreSlim _throttle = new(1, 1);

    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public ScreenScraperClient(IHttpClientFactory httpClientFactory, ILogger<ScreenScraperClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<ScreenScraperCallResult> GetUserInfoAsync(
        ScreenScraperCredentials credentials,
        CancellationToken cancellationToken) =>
        SendAsync("ssuserInfos.php", credentials, new Dictionary<string, string>(), cancellationToken);

    public Task<ScreenScraperCallResult> SearchAsync(
        ScreenScraperCredentials credentials,
        string searchTerm,
        int? systemId,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recherche"] = searchTerm,
        };

        if (systemId.HasValue)
        {
            parameters["systemeid"] = systemId.Value.ToString(CultureInfo.InvariantCulture);
        }

        return SendAsync("jeuRecherche.php", credentials, parameters, cancellationToken);
    }

    public Task<ScreenScraperCallResult> GetGameAsync(
        ScreenScraperCredentials credentials,
        string gameId,
        int? systemId,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gameid"] = gameId,
        };

        if (systemId.HasValue)
        {
            parameters["systemeid"] = systemId.Value.ToString(CultureInfo.InvariantCulture);
        }

        return SendAsync("jeuInfos.php", credentials, parameters, cancellationToken);
    }

    /// <summary>
    /// Downloads a media file. The URL must have been returned by the API and is validated
    /// against the ScreenScraper host before any request is made.
    /// </summary>
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
            _logger.LogWarning(
                "Media download failed with status {Status} for {Url}",
                (int)response.StatusCode,
                LogSanitizer.Sanitize(uri));
            return null;
        }

        // Copy into memory so the HttpResponseMessage can be disposed immediately; media files
        // are small (a few hundred KB at most).
        var buffer = new MemoryStream();
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// Only absolute https URLs pointing at screenscraper.fr are accepted, so a manipulated
    /// API response cannot make the launcher fetch arbitrary hosts.
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
        var allowed = host.Equals("screenscraper.fr", StringComparison.OrdinalIgnoreCase) ||
                      host.EndsWith(".screenscraper.fr", StringComparison.OrdinalIgnoreCase);

        if (!allowed)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private async Task<ScreenScraperCallResult> SendAsync(
        string endpoint,
        ScreenScraperCredentials credentials,
        IDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        if (!credentials.IsUsable)
        {
            return new ScreenScraperCallResult { Error = MetadataErrorKind.NotConfigured };
        }

        var query = BuildQuery(credentials, parameters);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ThrottleAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("ScreenScraper request {Endpoint} (attempt {Attempt}/{Max})", endpoint, attempt, MaxAttempts);

            HttpResponseMessage response;
            string body;

            try
            {
                response = await client.GetAsync(endpoint + "?" + query, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("ScreenScraper request failed: {Message}", LogSanitizer.Sanitize(ex.Message));

                if (attempt == MaxAttempts)
                {
                    return new ScreenScraperCallResult
                    {
                        Error = MetadataErrorKind.NetworkUnavailable,
                        Detail = LogSanitizer.Sanitize(ex.Message),
                    };
                }

                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("ScreenScraper request timed out");

                if (attempt == MaxAttempts)
                {
                    return new ScreenScraperCallResult { Error = MetadataErrorKind.ProviderUnavailable, Detail = "timeout" };
                }

                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("ScreenScraper rate limit reached (HTTP 429)");

                    if (attempt == MaxAttempts)
                    {
                        return new ScreenScraperCallResult { Error = MetadataErrorKind.RateLimited };
                    }

                    await BackoffAsync(attempt, cancellationToken, response.Headers.RetryAfter?.Delta).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("ScreenScraper rejected the credentials (HTTP {Status})", (int)response.StatusCode);
                    return new ScreenScraperCallResult { Error = MetadataErrorKind.InvalidCredentials };
                }

                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("ScreenScraper server error (HTTP {Status})", (int)response.StatusCode);

                    if (attempt == MaxAttempts)
                    {
                        return new ScreenScraperCallResult { Error = MetadataErrorKind.ProviderUnavailable };
                    }

                    await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // ScreenScraper answers a number of business errors with HTTP 400/404 and a
                // plain text body instead of JSON.
                if (!response.IsSuccessStatusCode)
                {
                    var classified = ClassifyTextError(body);
                    _logger.LogWarning(
                        "ScreenScraper returned HTTP {Status}, classified as {Error}",
                        (int)response.StatusCode,
                        classified);
                    return new ScreenScraperCallResult { Error = classified, Detail = Truncate(body) };
                }
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return new ScreenScraperCallResult { Error = MetadataErrorKind.NoResults };
            }

            var trimmed = body.TrimStart();
            if (!trimmed.StartsWith('{'))
            {
                var classified = ClassifyTextError(body);
                _logger.LogWarning("ScreenScraper returned a non-JSON body, classified as {Error}", classified);
                return new ScreenScraperCallResult { Error = classified, Detail = Truncate(body) };
            }

            try
            {
                var envelope = JsonSerializer.Deserialize<SsEnvelope>(body, JsonOptions);

                if (envelope is null)
                {
                    return new ScreenScraperCallResult { Error = MetadataErrorKind.InvalidResponse };
                }

                if (!string.IsNullOrWhiteSpace(envelope.Header?.Error))
                {
                    var classified = ClassifyTextError(envelope.Header.Error);
                    _logger.LogWarning("ScreenScraper reported an error, classified as {Error}", classified);
                    return new ScreenScraperCallResult { Error = classified, Detail = Truncate(envelope.Header.Error) };
                }

                return new ScreenScraperCallResult { Error = MetadataErrorKind.None, Envelope = envelope };
            }
            catch (JsonException ex)
            {
                _logger.LogError("ScreenScraper response could not be parsed: {Message}", LogSanitizer.Sanitize(ex.Message));
                return new ScreenScraperCallResult { Error = MetadataErrorKind.InvalidResponse, Detail = ex.Message };
            }
        }

        return new ScreenScraperCallResult { Error = MetadataErrorKind.ProviderUnavailable };
    }

    /// <summary>
    /// ScreenScraper communicates most problems as free text. The mapping below covers the
    /// messages the service is known to emit in French and English; anything unrecognised is
    /// reported as <see cref="MetadataErrorKind.Unknown"/> rather than being guessed at.
    /// </summary>
    internal static MetadataErrorKind ClassifyTextError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return MetadataErrorKind.Unknown;
        }

        var text = body.ToLowerInvariant();

        if (text.Contains("erreur de login", StringComparison.Ordinal) ||
            text.Contains("identifiant", StringComparison.Ordinal) ||
            text.Contains("mot de passe", StringComparison.Ordinal) ||
            text.Contains("login error", StringComparison.Ordinal) ||
            text.Contains("bad credentials", StringComparison.Ordinal) ||
            text.Contains("api totalement fermé", StringComparison.Ordinal) ||
            text.Contains("api closed", StringComparison.Ordinal))
        {
            return MetadataErrorKind.InvalidCredentials;
        }

        if (text.Contains("quota", StringComparison.Ordinal) ||
            text.Contains("maximum threads", StringComparison.Ordinal) ||
            text.Contains("trop de requ", StringComparison.Ordinal) ||
            text.Contains("too many", StringComparison.Ordinal) ||
            text.Contains("depasse", StringComparison.Ordinal) ||
            text.Contains("dépassé", StringComparison.Ordinal))
        {
            return MetadataErrorKind.RateLimited;
        }

        if (text.Contains("non trouve", StringComparison.Ordinal) ||
            text.Contains("non trouvée", StringComparison.Ordinal) ||
            text.Contains("not found", StringComparison.Ordinal) ||
            text.Contains("aucun", StringComparison.Ordinal) ||
            text.Contains("no result", StringComparison.Ordinal))
        {
            return MetadataErrorKind.NoResults;
        }

        if (text.Contains("fermé pour maintenance", StringComparison.Ordinal) ||
            text.Contains("maintenance", StringComparison.Ordinal) ||
            text.Contains("serveur", StringComparison.Ordinal))
        {
            return MetadataErrorKind.ProviderUnavailable;
        }

        return MetadataErrorKind.Unknown;
    }

    /// <summary>
    /// Builds the request query. Access tiers are additive and all optional:
    /// <list type="bullet">
    /// <item><c>softname</c> is always sent (the one field ScreenScraper always expects).</item>
    /// <item><c>devid</c>/<c>devpassword</c> are added when developer access is configured.</item>
    /// <item><c>ssid</c>/<c>sspassword</c> are added when a user account is configured.</item>
    /// </list>
    /// With none of the two credential pairs present the request is anonymous.
    /// </summary>
    internal static string BuildQuery(ScreenScraperCredentials credentials, IDictionary<string, string> parameters)
    {
        var pairs = new List<string>
        {
            "output=json",
            "softname=" + Uri.EscapeDataString(credentials.SoftwareName),
        };

        if (credentials.HasDeveloperCredentials)
        {
            pairs.Add("devid=" + Uri.EscapeDataString(credentials.DeveloperId));
            pairs.Add("devpassword=" + Uri.EscapeDataString(credentials.DeveloperPassword));
        }

        if (credentials.HasUserCredentials)
        {
            pairs.Add("ssid=" + Uri.EscapeDataString(credentials.UserName!));
            pairs.Add("sspassword=" + Uri.EscapeDataString(credentials.UserPassword!));
        }

        foreach (var (key, value) in parameters)
        {
            pairs.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
        }

        return string.Join('&', pairs);
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

    public void Dispose() => _throttle.Dispose();
}

/// <summary>Credential bundle passed to every request. Never logged.</summary>
public sealed class ScreenScraperCredentials
{
    public required string DeveloperId { get; init; }

    public required string DeveloperPassword { get; init; }

    public required string SoftwareName { get; init; }

    public string? UserName { get; init; }

    public string? UserPassword { get; init; }

    /// <summary>
    /// A request can always be attempted as long as a software name is present. Developer and
    /// user credentials are optional refinements — without them the request goes out
    /// anonymously and the server decides whether to serve it.
    /// </summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(SoftwareName);

    public bool HasDeveloperCredentials =>
        !string.IsNullOrWhiteSpace(DeveloperId) &&
        !string.IsNullOrWhiteSpace(DeveloperPassword);

    public bool HasUserCredentials =>
        !string.IsNullOrWhiteSpace(UserName) &&
        !string.IsNullOrWhiteSpace(UserPassword);
}
