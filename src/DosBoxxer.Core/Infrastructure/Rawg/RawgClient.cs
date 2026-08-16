using System.Globalization;
using System.Net;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Infrastructure.Rawg.Dto;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Rawg;

public sealed class RawgCallResult<T>
    where T : class
{
    private RawgCallResult(T? value, MetadataErrorKind error, string? detail)
    {
        Value = value;
        Error = error;
        Detail = detail;
    }

    public T? Value { get; }

    public MetadataErrorKind Error { get; }

    public string? Detail { get; }

    public bool IsSuccess => Error == MetadataErrorKind.None && Value is not null;

    public static RawgCallResult<T> Success(T value) => new(value, MetadataErrorKind.None, null);

    public static RawgCallResult<T> Failure(MetadataErrorKind error, string? detail = null) => new(null, error, detail);
}

/// <summary>
/// Low level access to the RAWG API.
///
/// Authentication is a single <c>key</c> query argument issued instantly on signup. The search
/// endpoint returns a light record per game; the detail endpoint adds the raw description and
/// the developers/publishers. Images are served from <c>media.rawg.io</c>. A conservative
/// minimum request interval keeps the launcher well inside RAWG's free quota.
/// </summary>
public sealed class RawgClient : IMediaHttpClient, IDisposable
{
    public const string HttpClientName = "rawg";
    public const string BaseAddress = "https://api.rawg.io/api/";

    // RAWG's free key is quota-limited per month rather than per second; a conservative 1.1s
    // spacing plus a rolling hourly cap keeps well within fair use, and a real 429 is still
    // handled with backoff. (A hard monthly cap cannot be enforced by spacing alone; the server
    // returns 429 when the monthly quota is exhausted.)
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1100);
    private const int MaxRequestsPerHour = 1000;
    private const int MaxAttempts = 3;
    private const long MaxMediaBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RawgClient> _logger;
    private readonly RateLimiter _rateLimiter =
        new(MinimumRequestInterval, MaxRequestsPerHour, TimeSpan.FromHours(1));

    public RawgClient(IHttpClientFactory httpClientFactory, ILogger<RawgClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<RawgCallResult<RawgGamesResponse>> SearchAsync(
        string apiKey,
        string title,
        int? platformId,
        CancellationToken cancellationToken)
    {
        var query = new List<(string, string)>
        {
            ("search", title),
            ("page_size", "20"),
            ("search_precise", "true"),
        };

        if (platformId.HasValue)
        {
            query.Add(("platforms", platformId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return GetAsync<RawgGamesResponse>("games", apiKey, query, cancellationToken);
    }

    public Task<RawgCallResult<RawgGame>> GetGameAsync(string apiKey, long gameId, CancellationToken cancellationToken) =>
        GetAsync<RawgGame>(
            "games/" + gameId.ToString(CultureInfo.InvariantCulture),
            apiKey,
            new List<(string, string)>(),
            cancellationToken);

    private async Task<RawgCallResult<T>> GetAsync<T>(
        string endpoint,
        string apiKey,
        IReadOnlyList<(string Key, string Value)> parameters,
        CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return RawgCallResult<T>.Failure(MetadataErrorKind.NotConfigured);
        }

        var query = BuildQuery(apiKey, parameters);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("RAWG request {Endpoint} (attempt {Attempt}/{Max})", endpoint, attempt, MaxAttempts);

            HttpResponseMessage response;
            string body;

            try
            {
                response = await client.GetAsync(endpoint + "?" + query, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("RAWG request failed: {Message}", LogSanitizer.Sanitize(ex.Message));
                if (attempt == MaxAttempts)
                {
                    return RawgCallResult<T>.Failure(MetadataErrorKind.NetworkUnavailable, LogSanitizer.Sanitize(ex.Message));
                }

                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == MaxAttempts)
                {
                    return RawgCallResult<T>.Failure(MetadataErrorKind.ProviderUnavailable, "timeout");
                }

                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt == MaxAttempts)
                    {
                        return RawgCallResult<T>.Failure(MetadataErrorKind.RateLimited);
                    }

                    await BackoffAsync(attempt, cancellationToken, response.Headers.RetryAfter?.Delta).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("RAWG rejected the API key (HTTP {Status})", (int)response.StatusCode);
                    return RawgCallResult<T>.Failure(MetadataErrorKind.InvalidCredentials);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return RawgCallResult<T>.Failure(MetadataErrorKind.NoResults);
                }

                if ((int)response.StatusCode >= 500)
                {
                    if (attempt == MaxAttempts)
                    {
                        return RawgCallResult<T>.Failure(MetadataErrorKind.ProviderUnavailable);
                    }

                    await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return RawgCallResult<T>.Failure(MetadataErrorKind.InvalidResponse, TryParseError(body));
                }
            }

            try
            {
                var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
                return value is null
                    ? RawgCallResult<T>.Failure(MetadataErrorKind.InvalidResponse)
                    : RawgCallResult<T>.Success(value);
            }
            catch (JsonException ex)
            {
                _logger.LogError("RAWG response could not be parsed: {Message}", LogSanitizer.Sanitize(ex.Message));
                return RawgCallResult<T>.Failure(MetadataErrorKind.InvalidResponse, ex.Message);
            }
        }

        return RawgCallResult<T>.Failure(MetadataErrorKind.ProviderUnavailable);
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
        var allowed = host.Equals("rawg.io", StringComparison.OrdinalIgnoreCase) ||
                      host.EndsWith(".rawg.io", StringComparison.OrdinalIgnoreCase);

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
            "key=" + Uri.EscapeDataString(apiKey),
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
            var error = JsonSerializer.Deserialize<RawgError>(body, JsonOptions);
            return error?.Error ?? error?.Detail;
        }
        catch (JsonException)
        {
            return body.Length <= 200 ? body : body[..200];
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

    public void Dispose() => _rateLimiter.Dispose();
}
