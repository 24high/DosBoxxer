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

    Task<bool> ShowEditGameAsync(Game game);

    Task<bool> ShowSettingsAsync();

    Task ShowLightboxAsync(IReadOnlyList<string> imagePaths, int startIndex);

    Task<RefreshMetadataChoice?> ShowRefreshMetadataAsync(Game game);

    /// <summary>Lets the user pick the matching entry from a provider search result list.</summary>
    Task<MetadataSearchResult?> ShowMetadataSearchAsync(string initialSearchTerm);
}
