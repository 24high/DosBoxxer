using System.Text.Json.Serialization;

namespace DosBoxxer.Core.Models;

public enum DoubleClickAction
{
    PlayGame = 0,
    ShowDetails = 1,
}

public enum CoverSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// Non-sensitive application settings. Persisted as JSON in the platform user data directory.
/// Secrets (ScreenScraper passwords, API keys) are intentionally NOT part of this object —
/// see <see cref="ScreenScraperSettings"/> and <c>ISecretStore</c>.
/// </summary>
public sealed class AppSettings
{
    // ---- DOSBox -----------------------------------------------------------------
    public string? DosBoxExecutablePath { get; set; }

    public string? BaseDosBoxConfigPath { get; set; }

    public string? DosBoxAdditionalArguments { get; set; }

    /// <summary>Append an <c>exit</c> command so DOSBox closes when the game terminates.</summary>
    public bool CloseDosBoxAfterExit { get; set; }

    // ---- Metadata provider ------------------------------------------------------
    /// <summary>
    /// Key of the active metadata provider: <c>mobygames</c>, <c>igdb</c> or <c>rawg</c>.
    /// The <c>ActiveMetadataProvider</c> facade forwards to the matching one.
    /// </summary>
    public string MetadataProviderKey { get; set; } = "mobygames";

    public ScreenScraperSettings ScreenScraper { get; set; } = new();

    public MobyGamesSettings MobyGames { get; set; } = new();

    public IgdbSettings Igdb { get; set; } = new();

    public RawgSettings Rawg { get; set; } = new();

    // ---- Cloud saves ------------------------------------------------------------
    public GoogleDriveSettings GoogleDrive { get; set; } = new();

    /// <summary>Optional path to an external savegame catalog JSON; falls back to the bundled one.</summary>
    public string? SavegameCatalogPath { get; set; }

    // ---- Localisation -----------------------------------------------------------
    /// <summary>BCP-47 language tag, e.g. <c>en</c>, <c>de</c>, <c>zh-Hans</c>.</summary>
    public string Language { get; set; } = "en";

    // ---- Library / UI preferences ----------------------------------------------
    public CoverSize CoverSize { get; set; } = CoverSize.Medium;

    public DoubleClickAction DoubleClickAction { get; set; } = DoubleClickAction.PlayGame;

    public bool ConfirmBeforeRemoving { get; set; } = true;

    public bool AutoFetchMetadata { get; set; } = true;

    public bool CacheScreenshots { get; set; } = true;

    public bool RememberWindowState { get; set; } = true;

    public bool ShowDetailsPane { get; set; } = true;

    public GameSortOrder SortOrder { get; set; } = GameSortOrder.TitleAscending;

    // ---- Window state -----------------------------------------------------------
    public WindowStateSettings Window { get; set; } = new();

    public AppSettings Clone() => new()
    {
        DosBoxExecutablePath = DosBoxExecutablePath,
        BaseDosBoxConfigPath = BaseDosBoxConfigPath,
        DosBoxAdditionalArguments = DosBoxAdditionalArguments,
        CloseDosBoxAfterExit = CloseDosBoxAfterExit,
        MetadataProviderKey = MetadataProviderKey,
        ScreenScraper = ScreenScraper.Clone(),
        MobyGames = MobyGames.Clone(),
        Igdb = Igdb.Clone(),
        Rawg = Rawg.Clone(),
        GoogleDrive = GoogleDrive.Clone(),
        SavegameCatalogPath = SavegameCatalogPath,
        Language = Language,
        CoverSize = CoverSize,
        DoubleClickAction = DoubleClickAction,
        ConfirmBeforeRemoving = ConfirmBeforeRemoving,
        AutoFetchMetadata = AutoFetchMetadata,
        CacheScreenshots = CacheScreenshots,
        RememberWindowState = RememberWindowState,
        ShowDetailsPane = ShowDetailsPane,
        SortOrder = SortOrder,
        Window = Window.Clone(),
    };
}

/// <summary>
/// ScreenScraper connection settings. The two password fields and the developer password are
/// never serialised into <c>settings.json</c>; they are held in memory after being read from
/// the secret store.
/// </summary>
public sealed class ScreenScraperSettings
{
    /// <summary>ScreenScraper developer id (<c>devid</c> request parameter).</summary>
    public string? DeveloperId { get; set; }

    /// <summary>Name of this software as registered with ScreenScraper (<c>softname</c>).</summary>
    public string SoftwareName { get; set; } = "DosBoxxer";

    /// <summary>ScreenScraper user account name (<c>ssid</c>).</summary>
    public string? UserName { get; set; }

    /// <summary>
    /// ScreenScraper system id used to restrict searches. 135 is "PC Dos" in the ScreenScraper
    /// system list; it can be changed here if the id ever changes. Set to <c>null</c> to search
    /// across all systems.
    /// </summary>
    public int? SystemId { get; set; } = 135;

    /// <summary>Preferred media region for covers/screenshots, e.g. <c>wor</c>, <c>eu</c>, <c>us</c>.</summary>
    public string PreferredRegion { get; set; } = "wor";

    /// <summary>Maximum number of screenshots downloaded per game.</summary>
    public int MaxScreenshots { get; set; } = 6;

    // -- transient, never serialised ------------------------------------------------
    [JsonIgnore]
    public string? DeveloperPassword { get; set; }

    [JsonIgnore]
    public string? UserPassword { get; set; }

    /// <summary>
    /// Developer / application access: a <c>devid</c>+<c>devpassword</c> pair issued by
    /// ScreenScraper. Preferred when present because it grants the application its own quota.
    /// </summary>
    [JsonIgnore]
    public bool HasDeveloperAccess =>
        !string.IsNullOrWhiteSpace(DeveloperId) &&
        !string.IsNullOrWhiteSpace(DeveloperPassword);

    /// <summary>End-user account (<c>ssid</c>+<c>sspassword</c>), which raises the request quota.</summary>
    [JsonIgnore]
    public bool HasUserAccount =>
        !string.IsNullOrWhiteSpace(UserName) &&
        !string.IsNullOrWhiteSpace(UserPassword);

    /// <summary>
    /// ScreenScraper is always usable: with developer access, with a user account, or fully
    /// anonymously. The only hard requirement is a non-empty software name, which always has a
    /// default. This is what lets metadata lookups work without any configuration.
    /// </summary>
    [JsonIgnore]
    public bool CanQuery => !string.IsNullOrWhiteSpace(SoftwareName);

    public ScreenScraperSettings Clone() => new()
    {
        DeveloperId = DeveloperId,
        SoftwareName = SoftwareName,
        UserName = UserName,
        SystemId = SystemId,
        PreferredRegion = PreferredRegion,
        MaxScreenshots = MaxScreenshots,
        DeveloperPassword = DeveloperPassword,
        UserPassword = UserPassword,
    };
}

/// <summary>
/// MobyGames connection settings. MobyGames authenticates with a single private API key, which
/// is issued on request and — like the ScreenScraper passwords — is never serialised into
/// <c>settings.json</c>; it is held in memory after being read from the secret store.
/// </summary>
public sealed class MobyGamesSettings
{
    /// <summary>MobyGames platform id to restrict searches to. 2 is "DOS".</summary>
    public int PlatformId { get; set; } = 2;

    /// <summary>Maximum number of screenshots imported per game.</summary>
    public int MaxScreenshots { get; set; } = 6;

    // -- transient, never serialised ------------------------------------------------
    [JsonIgnore]
    public string? ApiKey { get; set; }

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public MobyGamesSettings Clone() => new()
    {
        PlatformId = PlatformId,
        MaxScreenshots = MaxScreenshots,
        ApiKey = ApiKey,
    };
}

/// <summary>
/// IGDB connection settings. IGDB authenticates with a Twitch application: a public client id
/// and a client secret exchanged for a short-lived bearer token. The secret is held in memory
/// after being read from the secret store and is never serialised into <c>settings.json</c>.
/// </summary>
public sealed class IgdbSettings
{
    /// <summary>Twitch application client id (public).</summary>
    public string? ClientId { get; set; }

    /// <summary>IGDB platform id to restrict searches to. 13 is "DOS".</summary>
    public int PlatformId { get; set; } = 13;

    public int MaxScreenshots { get; set; } = 6;

    // -- transient, never serialised ------------------------------------------------
    [JsonIgnore]
    public string? ClientSecret { get; set; }

    [JsonIgnore]
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public IgdbSettings Clone() => new()
    {
        ClientId = ClientId,
        PlatformId = PlatformId,
        MaxScreenshots = MaxScreenshots,
        ClientSecret = ClientSecret,
    };
}

/// <summary>
/// RAWG connection settings. RAWG authenticates with a single API key issued instantly on
/// signup. The key is held in memory after being read from the secret store.
/// </summary>
public sealed class RawgSettings
{
    /// <summary>
    /// Optional RAWG platform id to filter searches. RAWG has no dedicated DOS platform, so this
    /// is empty by default and the search runs across all platforms.
    /// </summary>
    public int? PlatformId { get; set; }

    public int MaxScreenshots { get; set; } = 6;

    // -- transient, never serialised ------------------------------------------------
    [JsonIgnore]
    public string? ApiKey { get; set; }

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public RawgSettings Clone() => new()
    {
        PlatformId = PlatformId,
        MaxScreenshots = MaxScreenshots,
        ApiKey = ApiKey,
    };
}

/// <summary>
/// Google Drive cloud-save connection settings. The OAuth 2.0 "Desktop app" client id and client
/// secret are hard coded in the application (see the constants below) and are neither persisted
/// nor user-configurable. Only the connected account's e-mail is stored in <c>settings.json</c>;
/// the refresh token lives in the secret store.
/// </summary>
public sealed class GoogleDriveSettings
{
    /// <summary>OAuth 2.0 client id of the bundled "Desktop app" credential.</summary>
    public const string HardcodedClientId = "307242373440-brdi8av2t7lk701knmm9so459vsjdsjg.apps.googleusercontent.com";

    /// <summary>OAuth 2.0 client secret of the bundled "Desktop app" credential.</summary>
    public const string HardcodedClientSecret = "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";

    /// <summary>OAuth 2.0 client id. Defaults to the bundled credential; never serialised.</summary>
    [JsonIgnore]
    public string? ClientId { get; set; } = HardcodedClientId;

    /// <summary>Email of the connected account, shown in the UI. Informational only.</summary>
    public string? AccountEmail { get; set; }

    // -- transient, never serialised ------------------------------------------------
    [JsonIgnore]
    public string? ClientSecret { get; set; } = HardcodedClientSecret;

    /// <summary>
    /// True when an OAuth client is configured. This is the <c>cloudConfigured</c> half of the
    /// central <c>cloudConfigured &amp;&amp; cloudAuthenticated</c> gate.
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public GoogleDriveSettings Clone() => new()
    {
        ClientId = ClientId,
        AccountEmail = AccountEmail,
        ClientSecret = ClientSecret,
    };
}

public sealed class WindowStateSettings
{
    public double Width { get; set; } = 1360;

    public double Height { get; set; } = 860;

    public double? X { get; set; }

    public double? Y { get; set; }

    public bool IsMaximized { get; set; }

    public WindowStateSettings Clone() => new()
    {
        Width = Width,
        Height = Height,
        X = X,
        Y = Y,
        IsMaximized = IsMaximized,
    };
}
