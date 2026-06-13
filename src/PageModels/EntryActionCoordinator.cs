using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Extensions.Logging;
using AniSprinkles.Views;

namespace AniSprinkles.PageModels;

/// <summary>
/// Page-specific hooks for <see cref="EntryActionCoordinator"/>. Only <see cref="OpenDetailsAsync"/>
/// is required; everything else defaults to a no-op so list-free hosts (Discover/browse) and the
/// section-based My Anime page can each supply just what they need.
/// </summary>
public sealed class EntryActionHost
{
    /// <summary>Navigate to Media Details for the entry's media.</summary>
    public required Func<MediaListEntry, Task> OpenDetailsAsync { get; init; }

    /// <summary>Runs before any menu/flow opens (My Anime: flush the pending debounced +1 save).</summary>
    public Func<Task>? OnBeforeFlowAsync { get; init; }

    /// <summary>Optimistic UI removal before a move/delete round-trip (My Anime: drop from its section).</summary>
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
    /// (My Anime: forced reload).</summary>
    public Func<Task>? OnMutationFailedAsync { get; init; }

    /// <summary>Receives the <c>ErrorReportService.Record</c> reference string for the page's error details.</summary>
    public Action<string>? SetErrorDetails { get; init; }
}

/// <summary>
/// Shared long-press entry-action flows — the action menu, the move/add/rate/edit-progress/remove
/// popups, persistence, and toast/snackbar feedback — extracted from MyAnimePageModel so Discover,
/// View All, and search rows offer the same menu. Popups and side-effect rules stay in
/// <see cref="Services.ListEntryStatusFlow"/>; this class owns orchestration + saving, and the
/// hosting page model reacts through <see cref="EntryActionHost"/> callbacks.
/// One instance per page model (the completion-flow guard is per-host state).
/// </summary>
public sealed class EntryActionCoordinator(
    IAniListClient aniListClient,
    ErrorReportService errorReportService,
    ILogger logger,
    EntryActionHost host)
{
    private bool _isCompletionFlowActive;

    private static readonly Dictionary<MediaListStatus, string> StatusDisplayNames = new()
    {
        [MediaListStatus.Current] = "Watching",
        [MediaListStatus.Planning] = "Planning",
        [MediaListStatus.Completed] = "Completed",
        [MediaListStatus.Paused] = "Paused",
        [MediaListStatus.Dropped] = "Dropped",
        [MediaListStatus.Repeating] = "Rewatching",
    };

    // The toolkit's PopupBorder is the element that reaches the screen edge, so it must BE the visible
    // sheet: give it the rounded-top bottom-sheet shape (the popup's BackgroundColor fills it). A nested
    // rounded Border would leave the PopupBorder's own bottom strip transparent, showing the dim scrim.
    private static PopupOptions BottomSheetPopupOptions => new()
    {
        Shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
        {
            CornerRadius = new CornerRadius(20, 20, 0, 0),
            StrokeThickness = 0,
        },
        Shadow = null,
        CanBeDismissedByTappingOutsideOfPopup = true,
    };

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

        var popup = new ActionMenuPopup(entry.Media.DisplayTitle, BuildEntryActions(entry));
        var result = await Shell.Current.CurrentPage.ShowPopupAsync<object>(popup, BottomSheetPopupOptions, CancellationToken.None);
        if (result.WasDismissedByTappingOutsideOfPopup || result.Result is not MyAnimeEntryAction action)
        {
            return;
        }

        switch (action)
        {
            case MyAnimeEntryAction.OpenDetails:
                await host.OpenDetailsAsync(entry);
                break;
            case MyAnimeEntryAction.EditProgress:
                await HandleEditProgressAsync(entry);
                break;
            case MyAnimeEntryAction.MarkCompleted:
                await RunCompletionFlowAsync(entry);
                break;
            case MyAnimeEntryAction.Rate:
                await HandleRateAsync(entry);
                break;
            case MyAnimeEntryAction.MoveToList:
                await HandleMoveToListAsync(entry);
                break;
            case MyAnimeEntryAction.Remove:
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

        var popup = new MoveToListPopup(
            candidate.Media.DisplayTitle, currentStatus: null, allowRemove: false, subtitle: "Add to...");
        var result = await Shell.Current.CurrentPage.ShowPopupAsync<object>(popup, BottomSheetPopupOptions, CancellationToken.None);
        if (result.WasDismissedByTappingOutsideOfPopup || result.Result is not MediaListStatus targetStatus)
        {
            return;
        }

        await Services.ListEntryStatusFlow.ApplyStatusChangeAsync(candidate, targetStatus);

        SentrySdk.AddBreadcrumb($"Add to list confirmed (media {candidate.MediaId} → {targetStatus})", "list", "user");

        var title = candidate.Media.DisplayTitle;
        var targetName = StatusDisplayNames.GetValueOrDefault(targetStatus, targetStatus.ToString());

        try
        {
            await SaveAndAdoptIdAsync(candidate);
            await ShowToastAsync($"{title} added to {targetName}");
            await InvokeAsync(host.OnEntryStatusChangedAsync, candidate);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add media {MediaId} to {TargetStatus}", candidate.MediaId, targetStatus);
            await ShowFailureSnackbarAsync(ex, "Failed to add. Please try again.", retryAction: null);
            host.SetErrorDetails?.Invoke(errorReportService.Record(ex, "Add to list"));
        }
    }

    /// <summary>
    /// Shared completion flow (confirm + rating popups, save on confirm). Public because My Anime's
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

            var shouldSave = await Services.ListEntryStatusFlow.ApplyCompletionAsync(entry);
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

    private static IReadOnlyList<MyAnimeEntryAction> BuildEntryActions(MediaListEntry entry)
    {
        var actions = new List<MyAnimeEntryAction> { MyAnimeEntryAction.OpenDetails };

        if (entry.CanEditProgress)
        {
            actions.Add(MyAnimeEntryAction.EditProgress);
        }

        if (entry.CanMarkCompleted)
        {
            actions.Add(MyAnimeEntryAction.MarkCompleted);
        }

        actions.Add(MyAnimeEntryAction.Rate);
        actions.Add(MyAnimeEntryAction.MoveToList);
        actions.Add(MyAnimeEntryAction.Remove);
        return actions;
    }

    private async Task HandleMoveToListAsync(MediaListEntry entry)
    {
        if (entry.Media is null || entry.Status is null)
        {
            return;
        }

        var popup = new MoveToListPopup(entry.Media.DisplayTitle, entry.Status.Value);
        var result = await Shell.Current.CurrentPage.ShowPopupAsync<object>(popup, BottomSheetPopupOptions, CancellationToken.None);
        if (result.WasDismissedByTappingOutsideOfPopup || result.Result is null)
        {
            return;
        }

        if (result.Result is string action && action == "delete")
        {
            await HandleDeleteAsync(entry);
            return;
        }

        if (result.Result is MediaListStatus targetStatus)
        {
            await HandleMoveAsync(entry, targetStatus);
        }
    }

    private async Task HandleRateAsync(MediaListEntry entry)
    {
        var originalScore = entry.Score;
        if (await Services.ListEntryStatusFlow.ApplyRatingAsync(entry))
        {
            await SaveEntryInPlaceAsync(entry, () => entry.Score = originalScore, "Rate");
        }
    }

    private async Task HandleEditProgressAsync(MediaListEntry entry)
    {
        if (entry.Media is null || Shell.Current?.CurrentPage is not { } page)
        {
            return;
        }

        var popup = new EditProgressPopup(entry.Media.DisplayTitle, entry.Progress ?? 0, entry.MaxEpisodes);
        var options = new PopupOptions
        {
            Shape = null,
            Shadow = null,
            CanBeDismissedByTappingOutsideOfPopup = true,
        };

        var result = await page.ShowPopupAsync<object>(popup, options, CancellationToken.None);
        if (result.WasDismissedByTappingOutsideOfPopup || result.Result is not int newProgress)
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

        if (newProgress == (entry.Progress ?? 0))
        {
            return;
        }

        var originalProgress = entry.Progress;
        entry.Progress = newProgress;
        await SaveEntryInPlaceAsync(entry, () => entry.Progress = originalProgress, "Edit progress");
    }

    private async Task HandleDeleteAsync(MediaListEntry entry)
    {
        var title = entry.Media?.DisplayTitle ?? "this anime";
        var confirmed = await ConfirmPopup.ShowAsync(
            title: "Remove from List",
            message: $"Remove {title} from your list?",
            confirmText: "Remove",
            isDestructive: true,
            iconGlyph: FluentIconsRegular.Delete24);

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
            await ShowToastAsync($"{title} removed from list");
            await InvokeAsync(host.OnEntryRemovedAsync, entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete entry {EntryId} for media {MediaId}", entry.Id, entry.MediaId);
            // Capture the id so Retry re-runs the same delete.
            var retryId = entry.Id;
            await ShowFailureSnackbarAsync(
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
        var originalScore = entry.Score;
        var originalRepeat = entry.Repeat;

        await Services.ListEntryStatusFlow.ApplyStatusChangeAsync(entry, targetStatus);

        // Optimistic removal from source section.
        host.OnOptimisticRemove?.Invoke(entry);

        var targetName = StatusDisplayNames.GetValueOrDefault(targetStatus, targetStatus.ToString());

        try
        {
            await SaveAndAdoptIdAsync(entry);
            await ShowToastAsync($"{title} moved to {targetName}");
            await InvokeAsync(host.OnEntryStatusChangedAsync, entry);
        }
        catch (Exception ex)
        {
            // Revert entry state.
            entry.Status = originalStatus;
            entry.Progress = originalProgress;
            entry.Score = originalScore;
            entry.Repeat = originalRepeat;

            logger.LogError(ex, "Failed to move media {MediaId} to {TargetStatus}", entry.MediaId, targetStatus);
            // Move side effects were reverted, so there is no simple Retry path —
            // the user can long-press the entry again to retry.
            await ShowFailureSnackbarAsync(ex, "Failed to move. Please try again.", retryAction: null);
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
            await ShowToastAsync("Saved");
            await InvokeAsync(host.OnEntryStatusChangedAsync, entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save completed entry for media {MediaId}", entry.MediaId);
            // Capture the mutated entry so Retry re-saves the same state.
            var retryEntry = entry;
            await ShowFailureSnackbarAsync(
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
            await ShowToastAsync("Saved");
            await InvokeAsync(host.OnEntrySavedInPlaceAsync, entry);
        }
        catch (Exception ex)
        {
            revert();
            logger.LogError(ex, "Failed to save entry ({Context}) for media {MediaId}", context, entry.MediaId);
            await ShowFailureSnackbarAsync(ex, "Failed to save. Please try again.", retryAction: null);
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
            await ShowFailureSnackbarAsync(
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
            await ShowToastAsync($"{title} removed from list");
            await InvokeAsync(host.OnEntryRemovedAsync, entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Retry failed for delete entry {EntryId}", entryId);
            await ShowFailureSnackbarAsync(
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
        var saved = await aniListClient.SaveMediaListEntryAsync(entry);
        if (saved is { Id: > 0 })
        {
            entry.Id = saved.Id;
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

    private async Task ShowToastAsync(string message)
    {
        try
        {
            var toast = Toast.Make(message, ToastDuration.Short);
            await toast.Show();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Toast display failed");
        }
    }

    private async Task ShowSnackbarAsync(string message, Action? action = null)
    {
        try
        {
            var snackbar = Snackbar.Make(
                message,
                action: action,
                actionButtonText: action is null ? string.Empty : "Retry",
                duration: TimeSpan.FromSeconds(5));
            await snackbar.Show();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Snackbar display failed");
        }
    }

    /// <summary>
    /// Save-failure snackbar that adapts to the exception kind: during a service outage the global
    /// banner is already visible, so repeat the outage title and omit Retry (it won't work for a while).
    /// </summary>
    private Task ShowFailureSnackbarAsync(Exception ex, string fallbackMessage, Action? retryAction)
    {
        if (ex is AniListApiException { Kind: ApiErrorKind.ServiceOutage } apiEx)
        {
            return ShowSnackbarAsync(apiEx.UserTitle);
        }

        return ShowSnackbarAsync(fallbackMessage, action: retryAction);
    }
}
