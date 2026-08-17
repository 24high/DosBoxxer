namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Storage for sensitive values. Implementations must never write secrets to the log.
/// The interface exists so a platform specific keychain backend (DPAPI, libsecret, Keychain)
/// can replace the default file based store without touching any caller.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores or, when <paramref name="value"/> is <c>null</c>/empty, removes a secret.</summary>
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public static class SecretKeys
{
    public const string ScreenScraperDeveloperPassword = "screenscraper.devpassword";
    public const string ScreenScraperUserPassword = "screenscraper.userpassword";
    public const string MobyGamesApiKey = "mobygames.apikey";
    public const string IgdbClientSecret = "igdb.clientsecret";
    public const string RawgApiKey = "rawg.apikey";

    /// <summary>OAuth refresh token for the connected Google account.</summary>
    public const string GoogleDriveRefreshToken = "googledrive.refreshtoken";
}
