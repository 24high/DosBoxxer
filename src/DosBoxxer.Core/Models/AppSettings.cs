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
    public ScreenScraperSettings ScreenScraper { get; set; } = new();

    public MobyGamesSettings MobyGames { get; set; } = new();

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
        ScreenScraper = ScreenScraper.Clone(),
        MobyGames = MobyGames.Clone(),
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
