using System.Collections.Generic;
using System.Threading.Tasks;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;

namespace DosBoxxer.App.Services;

public enum FilePickerKind
{
    Any,
    Image,
    Executable,
    DosBoxConfig,
}

public sealed class RemoveGameChoice
{
    public bool Confirmed { get; init; }

    public bool DeleteCachedMedia { get; init; }
}

public sealed class RefreshMetadataChoice
{
    public required GameField Fields { get; init; }

    public bool OverwriteManualEdits { get; init; }

    /// <summary>Provider game id chosen by the user, or the one already stored on the game.</summary>
    public required string ProviderGameId { get; init; }
}

public enum BulkWizardDecision
{
    /// <summary>The user completed the wizard and the game was added.</summary>
    Added,

    /// <summary>The user skipped this game; the batch continues with the next one.</summary>
    Skipped,

    /// <summary>The user aborted the whole batch.</summary>
    Aborted,
}

public sealed class BulkWizardResult
{
    public required BulkWizardDecision Decision { get; init; }

    /// <summary>The created game when <see cref="Decision"/> is <see cref="BulkWizardDecision.Added"/>.</summary>
    public Game? Game { get; init; }
}

/// <summary>Context passed to the wizard when it runs as one step of a bulk import.</summary>
public sealed class BulkWizardContext
{
    public required string GameDirectory { get; init; }

    /// <summary>1-based position of this game within the batch.</summary>
    public required int Index { get; init; }

    public required int Total { get; init; }
}

/// <summary>
/// Everything the ViewModels need from the window layer. Keeping this behind an interface is
/// what allows the ViewModels to stay free of Avalonia types.
/// </summary>
public interface IDialogService
{
    Task<string?> PickFolderAsync(string title, string? startPath = null);

    Task<string?> PickFileAsync(string title, FilePickerKind kind, string? startPath = null);

    Task ShowMessageAsync(string title, string message, bool isError = false);

    Task<bool> ConfirmAsync(string title, string message, string? hint = null);

    Task<RemoveGameChoice> ConfirmRemoveGameAsync(Game game);

    Task<Game?> ShowAddGameWizardAsync();

    /// <summary>Runs the Add-Game wizard pre-seeded with a folder as one step of a bulk import.</summary>
    Task<BulkWizardResult> ShowAddGameWizardAsync(BulkWizardContext context);

    Task<bool> ShowEditGameAsync(Game game);

    Task<bool> ShowSettingsAsync();

    Task ShowLightboxAsync(IReadOnlyList<string> imagePaths, int startIndex);

    Task<RefreshMetadataChoice?> ShowRefreshMetadataAsync(Game game);

    /// <summary>Lets the user pick the matching entry from a provider search result list.</summary>
    Task<MetadataSearchResult?> ShowMetadataSearchAsync(string initialSearchTerm);
}
