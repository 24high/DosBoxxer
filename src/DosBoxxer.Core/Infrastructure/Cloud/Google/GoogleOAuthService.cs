using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Cloud.Google;

/// <summary>
/// Google OAuth 2.0 for a desktop application using the installed-app loopback redirect with
/// PKCE. The user authorises in their own browser — no Google password is ever entered inside the
/// launcher. Only the <c>drive.file</c> scope is requested (access limited to files this app
/// creates). The OAuth client credentials are hard coded in <see cref="Models.GoogleDriveSettings"/>;
/// the refresh token is kept in the secret store; access tokens are cached in memory and
/// refreshed transparently.
/// </summary>
public sealed class GoogleOAuthService : ICloudAuthService
{
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    public const string DriveScope = "https://www.googleapis.com/auth/drive.file";
    public const string HttpClientName = "GoogleApi";

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IPlatformService _platform;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GoogleOAuthService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _refreshToken;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiry = DateTimeOffset.MinValue;
    private bool _loaded;

    public GoogleOAuthService(
        ISettingsService settings,
        ISecretStore secrets,
        IPlatformService platform,
        IHttpClientFactory httpFactory,
        ILogger<GoogleOAuthService> logger)
    {
        _settings = settings;
        _secrets = secrets;
        _platform = platform;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public bool IsConfigured => _settings.Current.GoogleDrive.IsConfigured;

    public bool IsAuthenticated
    {
        get
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(_refreshToken);
        }
    }

    public string? AccountEmail => _settings.Current.GoogleDrive.AccountEmail;

    public event EventHandler? StateChanged;

    public async Task<CloudAuthResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var drive = _settings.Current.GoogleDrive;
        if (string.IsNullOrWhiteSpace(drive.ClientId) || string.IsNullOrWhiteSpace(drive.ClientSecret))
        {
            return new CloudAuthResult { Status = CloudAuthStatus.NotConfigured };
        }

        HttpListener? listener = null;
        try
        {
            var port = GetFreeLoopbackPort();
            var redirectUri = $"http://127.0.0.1:{port}/";

            listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            var verifier = CreateCodeVerifier();
            var challenge = CreateCodeChallenge(verifier);
            var state = CreateCodeVerifier();

            var authUrl =
                $"{AuthEndpoint}?client_id={Uri.EscapeDataString(drive.ClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                "&response_type=code" +
                $"&scope={Uri.EscapeDataString(DriveScope)}" +
                "&access_type=offline&prompt=consent" +
                $"&code_challenge={challenge}&code_challenge_method=S256" +
                $"&state={state}";

            if (!_platform.OpenUrl(authUrl))
            {
                return new CloudAuthResult { Status = CloudAuthStatus.Failed, Detail = "Could not open the browser" };
            }

            var context = await GetContextAsync(listener, cancellationToken).ConfigureAwait(false);
            var request = context.Request;
            var code = request.QueryString["code"];
            var error = request.QueryString["error"];
            var returnedState = request.QueryString["state"];

            await RespondAsync(context, error is null && code is not null).ConfigureAwait(false);

            if (error is not null)
            {
                return new CloudAuthResult
                {
                    Status = error.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
                        ? CloudAuthStatus.Denied
                        : CloudAuthStatus.Failed,
                    Detail = error,
                };
            }

            if (code is null || !string.Equals(returnedState, state, StringComparison.Ordinal))
            {
                return new CloudAuthResult { Status = CloudAuthStatus.Failed, Detail = "Invalid authorization response" };
            }

            var tokens = await ExchangeCodeAsync(drive.ClientId, drive.ClientSecret, code, verifier, redirectUri, cancellationToken)
                .ConfigureAwait(false);

            if (tokens?.RefreshToken is null)
            {
                return new CloudAuthResult { Status = CloudAuthStatus.Failed, Detail = "No refresh token returned" };
            }

            _refreshToken = tokens.RefreshToken;
            _accessToken = tokens.AccessToken;
            _accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn - 60);
            _loaded = true;

            await _secrets.SetAsync(SecretKeys.GoogleDriveRefreshToken, _refreshToken, cancellationToken).ConfigureAwait(false);

            RaiseStateChanged();
            return new CloudAuthResult { Status = CloudAuthStatus.Success };
        }
        catch (OperationCanceledException)
        {
            return new CloudAuthResult { Status = CloudAuthStatus.Cancelled };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during Google authorization");
            return new CloudAuthResult { Status = CloudAuthStatus.Network, Detail = ex.Message };
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or JsonException)
        {
            _logger.LogWarning(ex, "Google authorization failed");
            return new CloudAuthResult { Status = CloudAuthStatus.Failed, Detail = ex.Message };
        }
        finally
        {
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        EnsureLoaded();

        var token = _refreshToken;
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var client = _httpFactory.CreateClient(HttpClientName);
                using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("token", token) });
                await client.PostAsync(RevokeEndpoint, content, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                // Best effort — local state is cleared regardless so the account is disconnected.
                _logger.LogInformation("Token revocation could not be reached ({Type})", ex.GetType().Name);
            }
        }

        await ClearTokensAsync(cancellationToken).ConfigureAwait(false);
        RaiseStateChanged();
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        EnsureLoaded();

        if (string.IsNullOrEmpty(_refreshToken))
        {
            return null;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiry)
            {
                return _accessToken;
            }

            var drive = _settings.Current.GoogleDrive;
            if (string.IsNullOrWhiteSpace(drive.ClientId) || string.IsNullOrWhiteSpace(drive.ClientSecret))
            {
                return null;
            }

            var refreshed = await RefreshAsync(drive.ClientId, drive.ClientSecret, _refreshToken!, cancellationToken)
                .ConfigureAwait(false);

            if (refreshed?.AccessToken is null)
            {
                return null;
            }

            _accessToken = refreshed.AccessToken;
            _accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ---- token endpoint helpers -----------------------------------------------------------

    private async Task<TokenResponse?> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string verifier, string redirectUri, CancellationToken cancellationToken)
    {
        var client = _httpFactory.CreateClient(HttpClientName);
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("code_verifier", verifier),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
        });

        using var response = await client.PostAsync(TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<TokenResponse?> RefreshAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken cancellationToken)
    {
        var client = _httpFactory.CreateClient(HttpClientName);
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
        });

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Network problem: keep the refresh token, the next attempt may succeed.
            throw new CloudStorageException(Models.Cloud.SyncErrorKind.Network, ex.Message, ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                // invalid_grant → the authorization was revoked or expired. Clear state so the UI
                // reflects "not connected" instead of retrying forever.
                _logger.LogWarning("Google refused the refresh token; clearing the connection");
                await ClearTokensAsync(cancellationToken).ConfigureAwait(false);
                RaiseStateChanged();
                throw new CloudStorageException(Models.Cloud.SyncErrorKind.AuthRevoked, "The Google authorization was revoked");
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ClearTokensAsync(CancellationToken cancellationToken)
    {
        _refreshToken = null;
        _accessToken = null;
        _accessTokenExpiry = DateTimeOffset.MinValue;
        _loaded = true;

        await _secrets.RemoveAsync(SecretKeys.GoogleDriveRefreshToken, cancellationToken).ConfigureAwait(false);

        var settings = _settings.Current;
        if (settings.GoogleDrive.AccountEmail is not null)
        {
            settings.GoogleDrive.AccountEmail = null;
            await _settings.SaveCurrentAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            _refreshToken = _secrets.GetAsync(SecretKeys.GoogleDriveRefreshToken).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not load the stored Google refresh token");
            _refreshToken = null;
        }

        _loaded = true;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    // ---- loopback + PKCE primitives -------------------------------------------------------

    private static async Task<HttpListenerContext> GetContextAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        var contextTask = listener.GetContextAsync();
        var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        if (completed != contextTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await contextTask.ConfigureAwait(false);
    }

    private static async Task RespondAsync(HttpListenerContext context, bool success)
    {
        var message = success
            ? "DosBoxxer is now connected to Google Drive. You can close this tab and return to the app."
            : "Authorization failed. You can close this tab and return to DosBoxxer.";

        var html = Encoding.UTF8.GetBytes(
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>DosBoxxer</title></head>" +
            $"<body style=\"font-family:sans-serif;padding:2rem\"><h2>DosBoxxer</h2><p>{message}</p></body></html>");

        try
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = html.Length;
            await context.Response.OutputStream.WriteAsync(html).ConfigureAwait(false);
            context.Response.OutputStream.Close();
        }
        catch (HttpListenerException)
        {
        }
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 3600;
    }
}
