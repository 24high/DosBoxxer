using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Cloud;

namespace DosBoxxer.App.Services;

/// <summary>
/// Bridges the Core sync engine's <see cref="IConflictResolver"/> to the dialog layer. A sync runs
/// off the UI thread, so the conflict dialog is shown via the dispatcher and the decision is
/// marshalled back.
/// </summary>
public sealed class DialogConflictResolver : IConflictResolver
{
    private readonly IDialogService _dialogs;

    public DialogConflictResolver(IDialogService dialogs) => _dialogs = dialogs;

    public async Task<ConflictDecision?> ResolveAsync(
        IReadOnlyList<SyncPlanItem> conflicts,
        SyncTrigger trigger,
        CancellationToken cancellationToken = default) =>
        await Dispatcher.UIThread
            .InvokeAsync(() => _dialogs.ShowSyncConflictAsync(conflicts))
            .ConfigureAwait(false);
}
