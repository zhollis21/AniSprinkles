using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

/// <summary>
/// Page-specific hooks for <see cref="EntryActionCoordinator"/>. Only <see cref="OpenDetailsAsync"/>
/// is required; everything else defaults to a no-op so list-free hosts (Discover/browse) and the
/// section-based Library pages can each supply just what they need.
/// </summary>
public sealed class EntryActionHost
{
    /// <summary>Navigate to Media Details for the entry's media.</summary>
    public required Func<MediaListEntry, Task> OpenDetailsAsync { get; init; }

    /// <summary>Runs before any menu/flow opens (Library: flush the pending debounced +1 save).</summary>
    public Func<Task>? OnBeforeFlowAsync { get; init; }

    /// <summary>Optimistic UI removal before a move/delete round-trip (Library: drop from its section).</summary>
    public Action<MediaListEntry>? OnOptimisticRemove { get; init; }

    /// <summary>A save that did NOT change status succeeded (rate, progress edit). The entry's
    /// observable properties already carry the new values.</summary>
    public Func<MediaListEntry, Task>? OnEntrySavedInPlaceAsync { get; init; }

    /// <summary>A status-changing save succeeded (move, completion, add-to-list). The entry may now
    /// belong to a different list; <see cref="MediaListEntry.Id"/> is refreshed from the server.</summary>
    public Func<MediaListEntry, Task>? OnEntryStatusChangedAsync { get; init; }

    /// <summary>The entry was deleted from the user's list.</summary>
    public Func<MediaListEntry, Task>? OnEntryRemovedAsync { get; init; }

    /// <summary>A move/delete failed after <see cref="OnOptimisticRemove"/> ran — restore the UI
    /// (Library: forced reload).</summary>
    public Func<Task>? OnMutationFailedAsync { get; init; }

    /// <summary>Receives the <c>ErrorReportService.Record</c> reference string for the page's error details.</summary>
    public Action<string>? SetErrorDetails { get; init; }
}

/// <summary>
/// Shared long-press entry-action flows — the action menu, the move/add/rate/edit-progress/remove
/// popups, persistence, and toast/snackbar feedback — extracted from AnimeLibraryPageModel so Discover,
/// View All, and search rows offer the same menu. The popups themselves are behind
/// <see cref="IDialogService"/> and the side-effect rules stay in
/// <see cref="ListEntryStatusFlow"/>; this class owns orchestration + saving, and the
/// hosting page model reacts through <see cref="EntryActionHost"/> callbacks.
/// One instance per page model (the completion-flow guard is per-host state).
/// </summary>
public sealed class EntryActionCoordinator(
    IAniListClient aniListClient,
    ErrorReportService errorReportService,
    IDialogService dialogs,
    IUserFeedback feedback,
    ListEntryStatusFlow statusFlow,
    ILogger logger,
    EntryActionHost host)
{
    private bool _isCompletionFlowActive;

    /// <summary>Compact status name for the confirmation toasts, e.g. "moved to Watching" /
    /// "moved to Reading". Type-aware since #12; the entry carries which type it is.</summary>
    private static string StatusName(MediaListEntry entry, MediaListStatus status) =>
        MediaListVocabulary.StatusChipLabel(status, KindOf(entry));

    private static MediaKind KindOf(MediaListEntry entry) =>
        entry.Media?.IsManga is true ? MediaKind.Manga : MediaKind.Anime;

    /// <summary>Full action menu for an entry that is already on the user's list.</summary>
    public async Task ShowEntryMenuAsync(MediaListEntry entry)
    {
        if (entry?.Media is null || entry.Status is null)
        {
            return;
        }

        if (host.OnBeforeFlowAsync is not null)
        {
            await host.OnBeforeFlowAsync();
        }

        PerformLongPressHaptic();

        if (await dialogs.ShowEntryActionsAsync(entry.Media.DisplayTitle, BuildEntryActions(entry)) is not { } action)
        {
            return;
        }

        switch (action)
        {
            case MediaListEntryAction.OpenDetails:
                await host.OpenDetailsAsync(entry);
                break;
            case MediaListEntryAction.EditProgress:
                await HandleEditProgressAsync(entry);
                break;
            case MediaListEntryAction.MarkCompleted:
                await RunCompletionFlowAsync(entry);
                break;
            case MediaListEntryAction.Rate:
                await HandleRateAsync(entry);
                break;
            case MediaListEntryAction.MoveToList:
                await HandleMoveToListAsync(entry);
                break;
            case MediaListEntryAction.Remove:
                await HandleDeleteAsync(entry);
                break;
        }
    }

    /// <summary>
    /// Add-to-list flow for media NOT yet on the user's list: a status picker (no current status to
    /// omit, no Remove row), then a creating save — AniList's SaveMediaListEntry upserts by mediaId.
    /// The user's single not-on-list action, per the Discover design.
    /// </summary>
    public async Task ShowAddToListAsync(MediaListEntry candidate)
    {
        if (candidate?.Media is null)
        {
            return;
        }

        if (host.OnBeforeFlowAsync is not null)
        {
            await host.OnBeforeFlowAsync();
        }

        PerformLongPressHaptic();

        var choice = await dialogs.ShowMoveToListAsync(
            candidate.Media.DisplayTitle, currentStatus: null, kind: KindOf(candidate), allowRemove: false, subtitle: "Add to...");
        if (choice?.Status is not { } targetStatus)
        {
            return;
        }

        await statusFlow.ApplyStatusChangeAsync(candidate, targetStatus);

        SentrySdk.AddBreadcrumb($"Add to list confirmed (media {candidate.MediaId} → {targetStatus})", "list", "user");

        var title = candidate.Media.DisplayTitle;
        var targetName = StatusName(candidate, targetStatus);

        try
        {
            await SaveAndAdoptIdAsync(candidate);
            await feedback.ShowToastAsync($"{title} added to {targetName}");
            await InvokeAsync(host.OnEntryStatusChangedAsync, candidate);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add media {MediaId} to {TargetStatus}", candidate.MediaId, targetStatus);
            await feedback.ShowFailureSnackbarAsync(ex, "Failed to add. Please try again.", retryAction: null);
            host.SetErrorDetails?.Invoke(errorReportService.Record(ex, "Add to list"));
        }
    }

    /// <summary>
    /// Shared completion flow (confirm + rating popups, save on confirm). Public because Library’s
    /// +1-reaches-total path routes through it directly; guarded so overlapping triggers don't double-run.
    /// </summary>
    public async Task RunCompletionFlowAsync(MediaListEntry entry)
    {
        if (_isCompletionFlowActive)
        {
            return;
        }

        _isCompletionFlowActive = true;
        try
        {
            // Flush any pending debounced save first (may be for this or another entry).
            if (host.OnBeforeFlowAsync is not null)
            {
                await host.OnBeforeFlowAsync();
            }

            var shouldSave = await statusFlow.ApplyCompletionAsync(entry);
            if (shouldSave)
            {
                await SaveCompletedEntryAsync(entry);
            }
        }
        finally
        {
            _isCompletionFlowActive = false;
        }
    }

    private static IReadOnlyList<MediaListEntryAction> BuildEntryActions(MediaListEntry entry)
    {
        var actions = new List<MediaListEntryAction> { MediaListEntryAction.OpenDetails };

        if (entry.CanEditProgress)
        {
            actions.Add(MediaListEntryAction.EditProgress);
        }

        if (entry.CanMarkCompleted)
        {
            actions.Add(MediaListEntryAction.MarkCompleted);
        }

        actions.Add(MediaListEntryAction.Rate);
        actions.Add(MediaListEntryAction.MoveToList);
        actions.Add(MediaListEntryAction.Remove);
        return actions;
    }

    private async Task HandleMoveToListAsync(MediaListEntry entry)
    {
        if (entry.Media is null || entry.Status is null)
        {
            return;
        }

        if (await dialogs.ShowMoveToListAsync(entry.Media.DisplayTitle, entry.Status.Value, KindOf(entry)) is not { } choice)
        {
            return;
        }

        if (choice.IsRemove)
        {
            await HandleDeleteAsync(entry);
        }
        else if (choice.Status is { } targetStatus)
        {
            await HandleMoveAsync(entry, targetStatus);
        }
    }

    private async Task HandleRateAsync(MediaListEntry entry)
    {
        var originalScore = entry.Score;
        if (await statusFlow.ApplyRatingAsync(entry))
        {
            await SaveEntryInPlaceAsync(entry, () => entry.Score = originalScore, "Rate");
        }
    }

    private async Task HandleEditProgressAsync(MediaListEntry entry)
    {
        if (entry.Media is null)
        {
            return;
        }

        if (await dialogs.ShowEditProgressAsync(
                entry.Media.DisplayTitle,
                entry.ActiveProgress ?? 0,
                entry.ActiveProgressTotal,
                entry.ActiveProgressUnit) is not { } newProgress)
        {
            return;
        }

        await CommitProgressEditAsync(entry, newProgress);
    }

    private async Task CommitProgressEditAsync(MediaListEntry entry, int newProgress)
    {
        // Authoritative clamp (the popup also clamps for its UI, but the model owns the bounds).
        newProgress = entry.ClampProgress(newProgress);

        // Reaching the known total routes through the same completion flow as +1 EP (consistency).
        if (entry.IsCompletionAt(newProgress))
        {
            await RunCompletionFlowAsync(entry);
            return;
        }

        if (newProgress == (entry.ActiveProgress ?? 0))
        {
            return;
        }

        // Captured before the write: the unit is derived from the counters, so the revert has to
        // name the field it actually changed (#12).
        var unit = entry.ActiveProgressUnit;
        var originalProgress = entry.ProgressFor(unit);
        entry.SetProgressFor(unit, newProgress);
        await SaveEntryInPlaceAsync(entry, () => entry.SetProgressFor(unit, originalProgress), "Edit progress");
    }

    private async Task HandleDeleteAsync(MediaListEntry entry)
    {
        var title = entry.Media?.DisplayTitle ?? "this anime";
        var confirmed = await dialogs.ConfirmAsync(
            title: "Remove from List",
            message: $"Remove {title} from your list?",
            confirmText: "Remove",
            isDestructive: true,
            iconGlyph: Glyphs.Regular.Delete24);

        if (!confirmed)
        {
            return;
        }

        SentrySdk.AddBreadcrumb($"Remove from list confirmed (entry {entry.Id})", "list", "user");

        // Optimistic removal from UI.
        host.OnOptimisticRemove?.Invoke(entry);

        try
        {
            await aniListClient.DeleteMediaListEntryAsync(entry.Id);
            await feedback.ShowToastAsync($"{title} removed from list");
            await InvokeAsync(host.OnEntryRemovedAsync, entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete entry {EntryId} for media {MediaId}", entry.Id, entry.MediaId);
            // Capture the id so Retry re-runs the same delete.
            var retryId = entry.Id;
            await feedback.ShowFailureSnackbarAsync(
                ex,
                "Failed to remove. Please try again.",
                retryAction: () => _ = RetryDeleteEntryAsync(retryId, title, entry));
            host.SetErrorDetails?.Invoke(errorReportService.Record(ex, "Delete from list"));
            await InvokeAsync(host.OnMutationFailedAsync);
        }
    }

    private async Task HandleMoveAsync(MediaListEntry entry, MediaListStatus targetStatus)
    {
        var title = entry.Media?.DisplayTitle ?? "this anime";

        // Snapshot original state for rollback.
        var originalStatus = entry.Status;
        var originalProgress = entry.Progress;
        var originalProgressVolumes = entry.ProgressVolumes;
        var originalScore = entry.Score;
        var originalRepeat = entry.Repeat;

        await statusFlow.ApplyStatusChangeAsync(entry, targetStatus);

        // Optimistic removal from source section.
        host.OnOptimisticRemove?.Invoke(entry);

        var targetName = StatusName(entry, targetStatus);

        try
        {
            await SaveAndAdoptIdAsync(entry);
            await feedback.ShowToastAsync($"{title} moved to {targetName}");
            await InvokeAsync(host.OnEntryStatusChangedAsync, entry);
        }
        catch (Exception ex)
        {
            // Revert entry state.
            entry.Status = originalStatus;
            entry.Progress = originalProgress;
            // Restored alongside Progress: a Rereading move now zeroes both counters (#12).
            entry.ProgressVolumes = originalProgressVolumes;
            entry.Score = originalScore;
            entry.Repeat = originalRepeat;

            logger.LogError(ex, "Failed to move media {MediaId} to {TargetStatus}", entry.MediaId, targetStatus);
            // Move side effects were reverted, so there is no simple Retry path —
            // the user can long-press the entry again to retry.
            await feedback.ShowFailureSnackbarAsync(ex, "Failed to move. Please try again.", retryAction: null);
            host.SetErrorDetails?.Invoke(errorReportService.Record(ex, "Move to list"));
            await InvokeAsync(host.OnMutationFailedAsync);
        }
    }

    /// <summary>Saves a just-completed entry; status changed, so the host may need to re-section.</summary>
    private async Task SaveCompletedEntryAsync(MediaListEntry entry)
    {
        try
        {
            await SaveAndAdoptIdAsync(entry);
            await feedback.ShowToastAsync("Saved");
            await InvokeAsync(host.OnEntryStatusChangedAsync, entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save completed entry for media {MediaId}", entry.MediaId);
            // Capture the mutated entry so Retry re-saves the same state.
            var retryEntry = entry;
            await feedback.ShowFailureSnackbarAsync(
                ex,
                "Failed to save. Please try again.",
                retryAction: () => _ = RetrySaveCompletedEntryAsync(retryEntry));
            host.SetErrorDetails?.Invoke(errorReportService.Record(ex, "Save completed entry"));
        }
    }

    /// <summary>
    /// Saves an in-place edit (score or progress) that doesn't change which list the entry lives in,
    /// so no reload is needed — the observable property update refreshes any bound card. Reverts via
    /// <paramref name="revert"/> on failure.
    /// </summary>
    private async Task SaveEntryInPlaceAsync(MediaListEntry entry, Action revert, string context)
    {
        try
        {
            await SaveAndAdoptIdAsync(entry);
            await feedback.ShowToastAsync("Saved");
            await InvokeAsync(host.OnEntrySavedInPlaceAsync, entry);
        }
        catch (Exception ex)
        {
            revert();
            logger.LogError(ex, "Failed to save entry ({Context}) for media {MediaId}", context, entry.MediaId);
            await feedback.ShowFailureSnackbarAsync(ex, "Failed to save. Please try again.", retryAction: null);
            host.SetErrorDetails?.Invoke(errorReportService.Record(ex, context));
        }
    }

    private async Task RetrySaveCompletedEntryAsync(MediaListEntry entry)
    {
        try
        {
            await SaveAndAdoptIdAsync(entry);
            await InvokeAsync(host.OnEntryStatusChangedAsync, entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Retry failed for completed entry media {MediaId}", entry.MediaId);
            await feedback.ShowFailureSnackbarAsync(
                ex,
                "Failed to save. Please try again.",
                retryAction: () => _ = RetrySaveCompletedEntryAsync(entry));
            host.SetErrorDetails?.Invoke(errorReportService.Record(ex, "Retry save completed entry"));
        }
    }

    private async Task RetryDeleteEntryAsync(int entryId, string title, MediaListEntry entry)
    {
        try
        {
            await aniListClient.DeleteMediaListEntryAsync(entryId);
            await feedback.ShowToastAsync($"{title} removed from list");
            await InvokeAsync(host.OnEntryRemovedAsync, entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Retry failed for delete entry {EntryId}", entryId);
            await feedback.ShowFailureSnackbarAsync(
                ex,
                "Failed to remove. Please try again.",
                retryAction: () => _ = RetryDeleteEntryAsync(entryId, title, entry));
            host.SetErrorDetails?.Invoke(errorReportService.Record(ex, "Retry delete entry"));
        }
    }

    /// <summary>
    /// Persists the entry and adopts the server-assigned entry id — essential for the add-to-list
    /// path (the candidate starts with Id 0; a later Remove needs the real id).
    /// </summary>
    private async Task SaveAndAdoptIdAsync(MediaListEntry entry)
    {
        var wasCreate = entry.Id <= 0;
        var saved = await aniListClient.SaveMediaListEntryAsync(entry);
        if (saved is { Id: > 0 })
        {
            entry.Id = saved.Id;
        }
        else if (wasCreate)
        {
            // Creating an entry must yield a server id — a later Remove/Move needs it. A null or
            // id-less result means the create didn't really take, so fail loudly rather than show
            // a false success: the caller's catch surfaces the failure snackbar.
            throw new InvalidOperationException(
                "SaveMediaListEntry returned no entry id when creating a list entry.");
        }
    }

    private static Task InvokeAsync(Func<MediaListEntry, Task>? callback, MediaListEntry entry)
        => callback?.Invoke(entry) ?? Task.CompletedTask;

    private static Task InvokeAsync(Func<Task>? callback)
        => callback?.Invoke() ?? Task.CompletedTask;

    private static void PerformLongPressHaptic()
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch
        {
            // Haptic feedback is best-effort.
        }
    }
}
