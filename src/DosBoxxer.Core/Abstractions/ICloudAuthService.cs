namespace DosBoxxer.Core.Abstractions;

public enum CloudAuthStatus
{
    Success = 0,
    NotConfigured = 1,
    Cancelled = 2,
    Network = 3,
    Denied = 4,
    Failed = 5,
}

public sealed class CloudAuthResult
{
    public required CloudAuthStatus Status { get; init; }

    public string? AccountEmail { get; init; }

    public string? Detail { get; init; }

    public bool Success => Status == CloudAuthStatus.Success;
}

/// <summary>
/// OAuth 2.0 connection to the cloud provider (Google) for a desktop application. No password is
/// ever entered inside the launcher: authorisation happens in the system browser using the
/// installed-app loopback + PKCE flow. Only refresh/access tokens are held, in the secret store.
/// </summary>
public interface ICloudAuthService
{
    /// <summary>True when an OAuth client id is configured (from settings / environment).</summary>
    bool IsConfigured { get; }

    /// <summary>True when a refresh token is present, i.e. an account is connected.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Email of the connected account, when known.</summary>
    string? AccountEmail { get; }

    event EventHandler? StateChanged;

    /// <summary>Runs the browser authorisation flow and stores the resulting refresh token.</summary>
    Task<CloudAuthResult> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes the token with the provider (best effort) and clears all local token state.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a currently valid access token, transparently refreshing an expired one. Returns
    /// <c>null</c> when not authenticated or when the refresh failed (e.g. the grant was revoked);
    /// in the revoked case the local token state is cleared so the UI reflects "not connected".
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
