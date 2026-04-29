using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

public partial class MyAnimePageModel : ObservableObject
{
    private static readonly TimeSpan ListRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IncrementDebounceDelay = TimeSpan.FromMilliseconds(1500);
    private const string DetailsRoute = "media-details";

    // ── Persisted UI preferences (device-scoped, not cleared on sign-out) ──
    private const string ViewModePreferenceKey = "anime_view_mode";
    private const string SortFieldPreferenceKey = "anime_sort_field";
    private const string SortAscendingPreferenceKey = "anime_sort_ascending";

    private readonly IAniListClient _aniListClient;
    private readonly IAuthService _authService;
    private readonly IAiringNotificationService _airingNotificationService;
    private readonly ErrorReportService _errorReportService;
    private readonly IPreferences _preferences;
    private readonly INavigationService _navigationService;
    private readonly ILogger<MyAnimePageModel> _logger;
    private bool _hasLoaded;
    private DateTimeOffset _lastSuccessfulLoadUtc;

    // +1 debounce state: rapid taps batch into a single API call.
    private CancellationTokenSource? _incrementDebounceCts;
    private MediaListEntry? _pendingIncrementEntry;
    private int? _preIncrementProgress;
    private bool _isCompletionFlowActive;

    // ── Main page state (mutually exclusive) ────────────────────────
    // Transitions:
    //   AuthenticationPending → Unauthenticated | InitialLoading
    //   Unauthenticated       → InitialLoading (on sign-in)
    //   InitialLoading        → Content | Error
    //   Content               → Content (refresh keeps state)  | Unauthenticated (sign-out) | Error (first-load retry)
    //   Error                 → InitialLoading (retry)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.AuthenticationPending;

    // StateContainer.CurrentState is typed as string; null/empty restores default
    // children (the loaded content host). Non-Content states match a StateView key.
    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// True when the singleton ViewModel already has data that can be shown
    /// immediately (e.g. when a new page instance is created on back-navigation
    /// but the ViewModel's cached sections are still valid).
    /// </summary>
    public bool HasLoadedData => _hasLoaded && Sections.Count > 0;

    [ObservableProperty]
    private bool _isNavigatingToDetails;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _title = "My Anime";

    [ObservableProperty]
    private ObservableCollection<MediaListSection> _sections = [];

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    // ── Error state (full-page error view) ──────────────────────────
    // Visibility is driven by CurrentState == PageState.Error; the following
    // properties populate the error view template.
    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorSubtitle = string.Empty;

    [ObservableProperty]
    private string _errorIconGlyph = string.Empty;

    // ── Sort / Filter / View Mode ────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionGlyph))]
    private SortField _currentSortField = SortField.LastUpdated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionGlyph))]
    private bool _sortAscending;

    /// <summary>
    /// Icon glyph indicating current sort direction, shown on the active chip.
    /// </summary>
    public string SortDirectionGlyph => SortAscending
        ? FluentIconsRegular.ArrowUp24
        : FluentIconsRegular.ArrowDown24;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeIconGlyph))]
    private ListViewMode _currentViewMode = ListViewMode.Standard;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchVisible;

    /// <summary>
    /// True when all visible sections have zero filtered items and a search/filter is active.
    /// </summary>
    public bool HasNoResults => IsSearchVisible
        && !string.IsNullOrWhiteSpace(SearchText)
        && Sections.Count > 0
        && Sections.All(s => s.FilteredCount == 0);

    /// <summary>
    /// Icon glyph for the view mode toggle button, showing the CURRENT mode icon.
    /// </summary>
    public string ViewModeIconGlyph => CurrentViewMode switch
    {
        ListViewMode.Large => FluentIconsRegular.Grid24,
        ListViewMode.Compact => FluentIconsRegular.TextBulletListSquare24,
        _ => FluentIconsRegular.List24,
    };

    public MyAnimePageModel(IAniListClient aniListClient, IAuthService authService, IAiringNotificationService airingNotificationService, ErrorReportService errorReportService, IPreferences preferences, INavigationService navigationService, ILogger<MyAnimePageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _airingNotificationService = airingNotificationService;
        _errorReportService = errorReportService;
        _preferences = preferences;
        _navigationService = navigationService;
        _logger = logger;

        // Restore persisted UI preferences directly into backing fields to avoid
        // triggering partial property-changed handlers before the object is fully constructed.
        var savedMode = preferences.Get(ViewModePreferenceKey, nameof(ListViewMode.Large));
        if (Enum.TryParse<ListViewMode>(savedMode, out var restoredMode))
        {
            _currentViewMode = restoredMode;
        }

        var savedSort = preferences.Get(SortFieldPreferenceKey, nameof(SortField.LastUpdated));
        if (Enum.TryParse<SortField>(savedSort, out var restoredSort))
        {
            _currentSortField = restoredSort;
        }

        _sortAscending = preferences.Get(SortAscendingPreferenceKey, false);
    }

    public async Task LoadAsync(bool forceReload = false)
    {
        _logger.LogInformation(
            "MyAnime LoadAsync enter (forceReload={ForceReload}, isBusy={IsBusy}, hasLoaded={HasLoaded}, currentState={CurrentState}, hadSections={HadSections})",
            forceReload, IsBusy, _hasLoaded, CurrentState, Sections.Count);

        if (IsBusy)
        {
            _logger.LogInformation("MyAnime LoadAsync skipped: already busy.");
            return;
        }

        // Set IsBusy immediately — before any awaits — so concurrent callers
        // are rejected by the guard above. All cleanup happens in finally.
        IsBusy = true;
        var hadExistingSections = Sections.Count > 0;
        try
        {
            var token = await _authService.GetAccessTokenAsync();
            var isAuthenticated = !string.IsNullOrWhiteSpace(token);
            // OnAppearing can fire often; keep list navigation snappy by skipping refreshes inside a short stale window.
            var isFresh = _lastSuccessfulLoadUtc != default &&
                DateTimeOffset.UtcNow - _lastSuccessfulLoadUtc < ListRefreshInterval;

            if (_hasLoaded && !forceReload && isAuthenticated == IsAuthenticated)
            {
                if (!isAuthenticated || isFresh)
                {
                    return;
                }
            }

            if (forceReload)
            {
                _lastSuccessfulLoadUtc = default;
            }

            _logger.LogInformation("Loading My Anime list.");
            SentrySdk.AddBreadcrumb("Load My Anime list", "navigation", "state");

            IsAuthenticated = isAuthenticated;

            if (!IsAuthenticated)
            {
                ErrorDetails = string.Empty;
                Sections = [];
                _hasLoaded = true;
                _lastSuccessfulLoadUtc = default;
                CurrentState = PageState.Unauthenticated;
                return;
            }

            // Only show the full-page loading spinner when we have no content to
            // preserve. Pull-to-refresh while content is visible keeps CurrentState
            // at Content and surfaces progress via IsBusy on SfPullToRefresh.
            if (!hadExistingSections)
            {
                CurrentState = PageState.InitialLoading;
            }

            Title = "My Anime";
            ErrorDetails = string.Empty;

            // Sync display preferences from AniList before building the list so that
            // cross-device setting changes (title language, adult content, section order)
            // are always applied before sections are rendered.
            try
            {
                var viewer = await _aniListClient.GetViewerAsync();
                AppSettings.SyncFromViewer(viewer);
            }
            catch (Exception viewerEx)
            {
                _logger.LogWarning(viewerEx, "Failed to sync viewer preferences");
            }

            SentrySdk.AddBreadcrumb("Fetching AniList list", "http", "state");
            var groups = await _aniListClient.GetMyAnimeListGroupedAsync();
            // Capture current sort/filter state for section building.
            var sortField = CurrentSortField;
            var sortAsc = SortAscending;
            var filterText = SearchText;

            if (Sections.Count == 0)
            {
                // Cold path — no existing sections to diff against. Build off-thread because grouping
                // can be heavy on large lists, then publish in one assignment.
                var expandedStates = new Dictionary<string, bool>(StringComparer.Ordinal);
                var sections = await Task.Run(() => BuildSections(groups, expandedStates, sortField, sortAsc, filterText));
                Sections = sections;
            }
            else
            {
                // Warm path — mutate Sections in place. Work is proportional to what changed, not
                // to the total item count, which keeps steady-state pull-to-refresh off the GC path
                // that was driving the FocusEvent ANR storm.
                var mergeStart = Stopwatch.GetTimestamp();
                var result = MediaListSectionsMerger.Merge(
                    Sections,
                    groups,
                    AppSettings.AnimeSectionOrder,
                    AppSettings.DisplayAdultContent,
                    sortField,
                    sortAsc,
                    filterText);
                var mergeMs = Stopwatch.GetElapsedTime(mergeStart).TotalMilliseconds;
                _logger.LogDebug(
                    "MyAnime merge in {ElapsedMs:F1}ms: {SectionsAdded} sec+, {SectionsRemoved} sec-, {EntriesAdded} ent+, {EntriesRemoved} ent-, {EntriesMoved} moved, {EntriesUpdated} updated, {SectionsNeedingReset} reset",
                    mergeMs,
                    result.SectionsAdded,
                    result.SectionsRemoved,
                    result.EntriesAdded,
                    result.EntriesRemoved,
                    result.EntriesMoved,
                    result.EntriesUpdated,
                    result.SectionsNeedingReset);
            }

            OnPropertyChanged(nameof(HasNoResults));
            _hasLoaded = true;
            _lastSuccessfulLoadUtc = DateTimeOffset.UtcNow;
            CurrentState = PageState.Content;

            // Cache RELEASING media IDs for the background airing notification worker.
            CacheReleasingMediaIds(groups);

            // On first authenticated load, prompt for notification permission if not yet decided.
            // Status Unknown → shows dialog once. Granted/Denied → returns immediately on future loads.
            _ = RequestNotificationPermissionIfNeededAsync();
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;

            if (hadExistingSections && IsAuthenticated)
            {
                // Prefer stale data over blank UI when refresh fails after a previously successful load.
                // Pull-to-refresh is the retry path, so no action on the snackbar.
                await ShowSnackbarAsync(apiEx?.UserTitle ?? "Refresh failed. Showing cached list.");
                CurrentState = PageState.Content;
                _hasLoaded = true;
            }
            else
            {
                // Full-page error state — no cached data to fall back on.
                ErrorTitle = apiEx?.UserTitle ?? "Something Went Wrong";
                ErrorSubtitle = apiEx?.UserSubtitle ?? "An unexpected error occurred. Try again or check back later.";
                ErrorIconGlyph = apiEx?.IconGlyph ?? FluentIconsRegular.ErrorCircle24;
                CurrentState = PageState.Error;
                Sections = [];
                _hasLoaded = false;
            }

            _errorReportService.Record(ex, "Load My Anime");
            ErrorDetails = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Property change handlers ─────────────────────────────────────

    partial void OnCurrentStateChanged(PageState oldValue, PageState newValue)
        => _logger.LogInformation("PageState: {OldState} → {NewState} (key={StateKey})", oldValue, newValue, CurrentStateKey ?? "(null)");

    partial void OnSearchTextChanged(string value)
    {
        foreach (var section in Sections)
        {
            section.ApplyFilter(value);
        }

        OnPropertyChanged(nameof(HasNoResults));
    }

    partial void OnCurrentViewModeChanged(ListViewMode value)
        => _preferences.Set(ViewModePreferenceKey, value.ToString());

    partial void OnCurrentSortFieldChanged(SortField value)
    {
        _preferences.Set(SortFieldPreferenceKey, value.ToString());
        ApplySortToAllSections();
    }

    partial void OnSortAscendingChanged(bool value)
    {
        _preferences.Set(SortAscendingPreferenceKey, value);
        ApplySortToAllSections();
    }

    // ── Sort / Filter / View Mode commands ───────────────────────────

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible)
        {
            SearchText = string.Empty;
        }
    }

    [RelayCommand]
    private void CycleViewMode()
    {
        CurrentViewMode = CurrentViewMode switch
        {
            ListViewMode.Standard => ListViewMode.Large,
            ListViewMode.Large => ListViewMode.Compact,
            _ => ListViewMode.Standard
        };
    }

    [RelayCommand]
    private void SetSort(string? fieldName)
    {
        if (fieldName is null || !Enum.TryParse<SortField>(fieldName, out var field))
        {
            return;
        }

        if (field == CurrentSortField)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            CurrentSortField = field;
            // Title defaults ascending; everything else defaults descending.
            SortAscending = field is SortField.Title;
        }
    }

    private void ApplySortToAllSections()
    {
        foreach (var section in Sections)
        {
            section.ApplySort(CurrentSortField, SortAscending);
        }
    }

    // ── +1 Episode Increment ─────────────────────────────────────────

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task IncrementProgress(MediaListEntry? entry)
    {
        if (entry?.Media is null)
        {
            return;
        }

        // Dimmed-but-visible +1 pill (user has caught up) still receives taps. Tell the
        // user why nothing happened so a repeated tap doesn't feel like a broken button.
        if (!entry.CanIncrementProgress)
        {
            if (entry.ShouldShowIncrementButton)
            {
                await ShowToastAsync("You're caught up");
            }

            return;
        }

        var newProgress = (entry.Progress ?? 0) + 1;

        // ── Completion flow: only when we know the total. Shows confirm + rating
        // popups, then saves immediately (no debounce). Long-running airing shows
        // without a declared episode count fall through to the normal +1 path.
        if (entry.HasKnownEpisodeCount && entry.MaxEpisodes is { } totalEpisodes && newProgress >= totalEpisodes)
        {
            if (_isCompletionFlowActive)
            {
                return;
            }

            _isCompletionFlowActive = true;
            try
            {
                // Flush any pending debounced save first (may be for this or another entry).
                await FlushPendingIncrementAsync();

                var shouldSave = await ListEntryStatusFlow.ApplyCompletionAsync(entry);
                if (shouldSave)
                {
                    try
                    {
                        await _aniListClient.SaveMediaListEntryAsync(entry);
                        await ShowToastAsync("Saved");
                        await LoadAsync(forceReload: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to save completed entry for media {MediaId}", entry.MediaId);
                        // Capture the mutated entry so Retry re-saves the same state.
                        var retryEntry = entry;
                        await ShowFailureSnackbarAsync(
                            ex,
                            "Failed to save. Please try again.",
                            retryAction: () => _ = RetrySaveCompletedEntryAsync(retryEntry));
                        ErrorDetails = _errorReportService.Record(ex, "Increment progress (complete)");
                    }
                }
            }
            finally
            {
                _isCompletionFlowActive = false;
            }

            return;
        }

        // ── Normal +1 flow: optimistic UI update + debounced save ───
        // If switching to a different entry, flush the previous pending save.
        if (_pendingIncrementEntry is not null && _pendingIncrementEntry != entry)
        {
            await FlushPendingIncrementAsync();
        }

        // Track the progress before the first tap in this debounce series
        // so we can revert all the way back on failure.
        if (_pendingIncrementEntry != entry)
        {
            _preIncrementProgress = entry.Progress;
            _pendingIncrementEntry = entry;
        }

        entry.Progress = newProgress;

        // Immediately tell the user they've caught up the moment the last available
        // +1 lands. This is instant (pre-debounce) so they see it before scrolling away.
        // Finite-total shows don't reach here — they go through the completion flow above.
        if (!entry.HasKnownEpisodeCount && !entry.CanIncrementProgress)
        {
            await ShowToastAsync("You're caught up!");
        }

        _logger.LogInformation(
            "+1 debounce: media {MediaId} '{Title}' progress → {New} (original: {Original})",
            entry.MediaId, entry.Media.DisplayTitle, newProgress, _preIncrementProgress);

        // Cancel any pending debounce timer and start a new one.
        _incrementDebounceCts?.Cancel();
        _incrementDebounceCts = new CancellationTokenSource();
        var token = _incrementDebounceCts.Token;

        try
        {
            await Task.Delay(IncrementDebounceDelay, token);
        }
        catch (TaskCanceledException)
        {
            // Another tap came in — this save is superseded.
            return;
        }

        // Debounce period elapsed with no new taps; save now.
        await SavePendingIncrementAsync();
    }

    /// <summary>
    /// Immediately saves any pending debounced +1 increment (e.g. before navigation
    /// or when the completion flow triggers).
    /// </summary>
    private async Task FlushPendingIncrementAsync()
    {
        _incrementDebounceCts?.Cancel();
        _incrementDebounceCts = null;

        if (_pendingIncrementEntry is not null)
        {
            await SavePendingIncrementAsync();
        }
    }

    private async Task SavePendingIncrementAsync()
    {
        var entry = _pendingIncrementEntry;
        var originalProgress = _preIncrementProgress;

        _pendingIncrementEntry = null;
        _preIncrementProgress = null;

        if (entry is null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("+1 saving: media {MediaId} progress {Progress}", entry.MediaId, entry.Progress);
            await _aniListClient.SaveMediaListEntryAsync(entry);
            _logger.LogInformation("+1 saved: media {MediaId} progress {Progress}", entry.MediaId, entry.Progress);
            await ShowToastAsync("Saved");
        }
        catch (Exception ex)
        {
            // Revert to the progress before the entire debounce series.
            if (originalProgress.HasValue)
            {
                entry.Progress = originalProgress.Value;
            }

            _logger.LogError(ex, "Failed to save progress for media {MediaId}, reverted to {Progress}", entry.MediaId, originalProgress);
            // Progress was reverted, so there is no simple Retry path — the user can just tap +1 again.
            await ShowFailureSnackbarAsync(ex, "Failed to save. Please try again.", retryAction: null);
            ErrorDetails = _errorReportService.Record(ex, "Increment progress");
        }
    }

    // ── Long-press context menu (move between lists) ───────────────

    private static readonly Dictionary<MediaListStatus, string> StatusDisplayNames = new()
    {
        [MediaListStatus.Current] = "Watching",
        [MediaListStatus.Planning] = "Planning",
        [MediaListStatus.Completed] = "Completed",
        [MediaListStatus.Paused] = "Paused",
        [MediaListStatus.Dropped] = "Dropped",
        [MediaListStatus.Repeating] = "Rewatching",
    };

    [RelayCommand]
    private async Task ShowMoveMenu(MediaListEntry? entry)
    {
        if (entry?.Media is null || entry.Status is null)
        {
            return;
        }

        await FlushPendingIncrementAsync();

        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch
        {
            // Haptic feedback is best-effort.
        }

        var popup = new Views.MoveToListPopup(entry.Media.DisplayTitle, entry.Status.Value);
        var popupOptions = new PopupOptions
        {
            Shape = null,
            Shadow = null,
            CanBeDismissedByTappingOutsideOfPopup = true,
        };

        var result = await Shell.Current.CurrentPage.ShowPopupAsync<object>(popup, popupOptions, CancellationToken.None);
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

    private async Task HandleDeleteAsync(MediaListEntry entry)
    {
        var title = entry.Media?.DisplayTitle ?? "this anime";
        var confirmed = await Views.ConfirmPopup.ShowAsync(
            title: "Remove from List",
            message: $"Remove {title} from your list?",
            confirmText: "Remove",
            isDestructive: true,
            iconGlyph: FluentIconsRegular.Delete24);

        if (!confirmed)
        {
            return;
        }

        SentrySdk.AddBreadcrumb($"Remove from list confirmed (My Anime, entry {entry.Id})", "list", "user");

        // Optimistic removal from UI.
        RemoveEntryFromCurrentSection(entry);

        try
        {
            await _aniListClient.DeleteMediaListEntryAsync(entry.Id);
            await ShowToastAsync($"{title} removed from list");
            await LoadAsync(forceReload: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete entry {EntryId} for media {MediaId}", entry.Id, entry.MediaId);
            // Capture the id so Retry re-runs the same delete.
            var retryId = entry.Id;
            var retryTitle = title;
            await ShowFailureSnackbarAsync(
                ex,
                "Failed to remove. Please try again.",
                retryAction: () => _ = RetryDeleteEntryAsync(retryId, retryTitle));
            ErrorDetails = _errorReportService.Record(ex, "Delete from list");
            await LoadAsync(forceReload: true);
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

        await ListEntryStatusFlow.ApplyStatusChangeAsync(entry, targetStatus);

        // Optimistic removal from source section.
        RemoveEntryFromCurrentSection(entry);

        var targetName = StatusDisplayNames.GetValueOrDefault(targetStatus, targetStatus.ToString());

        try
        {
            await _aniListClient.SaveMediaListEntryAsync(entry);
            await ShowToastAsync($"{title} moved to {targetName}");
            await LoadAsync(forceReload: true);
        }
        catch (Exception ex)
        {
            // Revert entry state.
            entry.Status = originalStatus;
            entry.Progress = originalProgress;
            entry.Score = originalScore;
            entry.Repeat = originalRepeat;

            _logger.LogError(ex, "Failed to move media {MediaId} to {TargetStatus}", entry.MediaId, targetStatus);
            // Move side effects were reverted, so there is no simple Retry path —
            // the user can long-press the entry again to retry.
            await ShowFailureSnackbarAsync(ex, "Failed to move. Please try again.", retryAction: null);
            ErrorDetails = _errorReportService.Record(ex, "Move to list");
            await LoadAsync(forceReload: true);
        }
    }

    private void RemoveEntryFromCurrentSection(MediaListEntry entry)
    {
        foreach (var section in Sections)
        {
            if (section.ContainsEntry(entry))
            {
                section.RemoveItem(entry);
                break;
            }
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
            _logger.LogWarning(ex, "Toast display failed");
        }
    }

    private async Task ShowSnackbarAsync(
        string message,
        Action? action = null,
        string actionText = "Retry",
        TimeSpan? duration = null)
    {
        try
        {
            var snackbar = Snackbar.Make(
                message,
                action: action,
                actionButtonText: action is null ? string.Empty : actionText,
                duration: duration ?? TimeSpan.FromSeconds(5));
            await snackbar.Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snackbar display failed");
        }
    }

    /// <summary>
    /// Shows a save-failure snackbar that adapts to the exception kind: during a
    /// service outage the global banner is already visible, so the snackbar repeats
    /// the outage title and omits the Retry action (retrying won't work for minutes
    /// or hours). All other exception kinds keep the normal retry flow.
    /// </summary>
    private Task ShowFailureSnackbarAsync(Exception ex, string fallbackMessage, Action? retryAction)
    {
        if (ex is AniListApiException { Kind: ApiErrorKind.ServiceOutage } apiEx)
        {
            return ShowSnackbarAsync(apiEx.UserTitle);
        }

        return ShowSnackbarAsync(fallbackMessage, action: retryAction);
    }

    private async Task RetrySaveCompletedEntryAsync(MediaListEntry entry)
    {
        try
        {
            await _aniListClient.SaveMediaListEntryAsync(entry);
            await LoadAsync(forceReload: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry failed for completed entry media {MediaId}", entry.MediaId);
            await ShowFailureSnackbarAsync(
                ex,
                "Failed to save. Please try again.",
                retryAction: () => _ = RetrySaveCompletedEntryAsync(entry));
            ErrorDetails = _errorReportService.Record(ex, "Retry save completed entry");
        }
    }

    private async Task RetryDeleteEntryAsync(int entryId, string title)
    {
        try
        {
            await _aniListClient.DeleteMediaListEntryAsync(entryId);
            await ShowToastAsync($"{title} removed from list");
            await LoadAsync(forceReload: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry failed for delete entry {EntryId}", entryId);
            await ShowFailureSnackbarAsync(
                ex,
                "Failed to remove. Please try again.",
                retryAction: () => _ = RetryDeleteEntryAsync(entryId, title));
            ErrorDetails = _errorReportService.Record(ex, "Retry delete entry");
        }
    }

    // ── Pull to refresh ──────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh()
    {
        await FlushPendingIncrementAsync();
        await LoadAsync(forceReload: true);
    }

    [RelayCommand]
    private async Task RetryLoad()
    {
        CurrentState = PageState.InitialLoading;
        await LoadAsync(forceReload: true);
    }

    // ── Auth commands ────────────────────────────────────────────────

    [RelayCommand]
    private async Task SignIn()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _logger.LogInformation("Sign-in requested.");
            SentrySdk.AddBreadcrumb("Sign-in requested", "auth", "user");
            var signedIn = await _authService.SignInAsync();
            if (!signedIn)
            {
                await ShowToastAsync("Sign in canceled.");
                SentrySdk.AddBreadcrumb("Sign-in canceled", "auth", "user");
                return;
            }

            SentrySdk.AddBreadcrumb("Sign-in successful", "auth", "user");
        }
        catch (Exception ex)
        {
            await ShowSnackbarAsync("Sign in failed. Tap Details for more.");
            ErrorDetails = _errorReportService.Record(ex, "Sign in");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(forceReload: true);
    }

    [RelayCommand]
    private async Task SignOut()
    {
        _logger.LogInformation("Sign-out requested.");
        SentrySdk.AddBreadcrumb("Sign-out requested", "auth", "user");
        _airingNotificationService.CancelPeriodicCheck();
        _airingNotificationService.ClearNotificationState();
        await _authService.SignOutAsync();
        AppSettings.Clear();
        await LoadAsync(forceReload: true);
    }

    // ── Navigation ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenDetails(MediaListEntry? entry)
    {
        if (entry is null || IsNavigatingToDetails)
        {
            return;
        }

        // Flush any pending +1 debounce so the details page shows fresh data.
        await FlushPendingIncrementAsync();

        var mediaId = entry.MediaId != 0 ? entry.MediaId : entry.Media?.Id ?? 0;
        if (mediaId <= 0)
        {
            await ShowToastAsync("Unable to open details.");
            return;
        }

        // SelectionChanged can fire again before navigation completes on fast repeat taps.
        // Gate route pushes so we do not stack multiple details pages for the same user action.
        IsNavigatingToDetails = true;
        try
        {
            var navTraceId = $"{mediaId}-{Environment.TickCount64}";
            var navStartUtc = DateTimeOffset.UtcNow;
            var navStopwatch = Stopwatch.StartNew();
            _logger.LogInformation(
                "NAVTRACE {TraceId} tap accepted for media {MediaId} at {StartedUtc:O}",
                navTraceId,
                mediaId,
                navStartUtc);
            SentrySdk.AddBreadcrumb($"Open details {mediaId}", "navigation", "state");
            // Keep route payload minimal so navigation is not blocked by passing a full list-entry graph.
            // Use non-animated transition: the details page shows its own loading shell immediately,
            // and disabling the slide transition allows destination page to render without animation overhead.
            await _navigationService.GoToAsync(DetailsRoute, animate: false, new Dictionary<string, object>
            {
                ["mediaId"] = mediaId,
                ["navTraceId"] = navTraceId,
                ["navStartUtcTicks"] = navStartUtc.UtcTicks
            });
            navStopwatch.Stop();
            _logger.LogInformation(
                "NAVTRACE {TraceId} GoToAsync completed in {ElapsedMs}ms",
                navTraceId,
                navStopwatch.ElapsedMilliseconds);
        }
        finally
        {
            IsNavigatingToDetails = false;
        }
    }

    // ── Notification permission prompt ──────────────────────────────────

    // Tracks whether we've already shown the My Anime permission prompt so we don't re-prompt
    // on every list load after a denial. Cleared on sign-out so a fresh session gets the prompt.
    private const string PermissionPromptedPrefKey = "airing_permission_prompted";

    /// <summary>
    /// Called after the first successful authenticated list load. On API 33+ (where
    /// POST_NOTIFICATIONS requires a runtime dialog), shows the permission prompt once and
    /// syncs the result to AniList. On API &lt;33 (no runtime permission needed), respects the
    /// existing AniList value — schedules WorkManager if already enabled, does nothing otherwise.
    /// </summary>
    private async Task RequestNotificationPermissionIfNeededAsync()
    {
        try
        {
            // Only prompt once from My Anime. Settings can re-prompt via the explicit toggle.
            if (_preferences.Get(PermissionPromptedPrefKey, false))
            {
                return;
            }

            // On API <33, POST_NOTIFICATIONS is not a runtime permission — RequestPermissionAsync
            // returns true automatically. Don't sync to AniList in this case (the user didn't
            // explicitly opt in via a dialog). Instead, respect the existing AniList value.
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                try
                {
                    var viewer = await _aniListClient.GetViewerAsync();
                    if (viewer.Options.AiringNotifications)
                    {
                        _airingNotificationService.SchedulePeriodicCheck();
                    }
                }
                catch (Exception ex)
                {
                    // Don't set the prompted flag on failure — allow retry on next load.
                    _logger.LogWarning(ex, "Failed to check AniList airing notifications setting on API <33");
                    return;
                }

                _preferences.Set(PermissionPromptedPrefKey, true);
                return;
            }

            // Mark as prompted before awaiting the system dialog so concurrent/rapid loads
            // don't double-prompt. The permission dialog itself is a one-shot system UI —
            // even if the AniList sync afterward fails, the prompt already happened.
            _preferences.Set(PermissionPromptedPrefKey, true);

            bool granted = await _airingNotificationService.RequestPermissionAsync();

            // Sync the result to AniList so the Settings toggle stays in sync with device reality.
            // Fetch current viewer settings first so we don't overwrite any other preferences.
            try
            {
                var viewer = await _aniListClient.GetViewerAsync();
                var request = new UpdateUserRequest
                {
                    TitleLanguage = viewer.Options.TitleLanguage,
                    DisplayAdultContent = viewer.Options.DisplayAdultContent,
                    AiringNotifications = granted,
                    ScoreFormat = viewer.ScoreFormat,
                    StaffNameLanguage = viewer.Options.StaffNameLanguage,
                    RestrictMessagesToFollowing = viewer.Options.RestrictMessagesToFollowing,
                    ActivityMergeTime = viewer.Options.ActivityMergeTime,
                    NotificationOptions = viewer.Options.NotificationOptions
                        .Select(n => new NotificationOptionInput { Type = n.Type, Enabled = n.Enabled })
                        .ToList()
                };

                await _aniListClient.UpdateUserAsync(request);
                _logger.LogInformation("AiringNotifications={Granted} synced to AniList after permission prompt", granted);
            }
            catch (Exception ex)
            {
                // Non-fatal: WorkManager state is still correct. AniList sync can be corrected via Settings.
                _logger.LogWarning(ex, "Failed to sync AiringNotifications={Granted} to AniList after permission prompt", granted);
            }

            if (!granted)
            {
                return;
            }

            _airingNotificationService.SchedulePeriodicCheck();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to request notification permission on list load");
        }
    }

    // ── Airing notification cache ─────────────────────────────────────

    /// <summary>
    /// Saves the media IDs of currently-airing ("RELEASING") anime from the user's
    /// Watching and Planning lists to Preferences so the background <c>AiringCheckWorker</c>
    /// can poll AniList's AiringSchedule API without fetching the full list.
    /// Planning is included so users are notified when a show they intend to watch airs.
    /// </summary>
    private void CacheReleasingMediaIds(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups)
    {
        var releasingIds = groups
            .Where(g => g.Name is "Watching" or "Rewatching" or "Planning")
            .SelectMany(g => g.Entries)
            .Where(e => e.Media?.Status is "RELEASING")
            .Select(e => e.MediaId)
            .Distinct()
            .ToList();

        _preferences.Set("airing_media_ids", string.Join(",", releasingIds));
    }

    // ── Section building ─────────────────────────────────────────────

    private static ObservableCollection<MediaListSection> BuildSections(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups,
        IReadOnlyDictionary<string, bool> expandedStates,
        SortField sortField,
        bool sortAscending,
        string filterText)
    {
        var sections = new ObservableCollection<MediaListSection>();

        var orderedGroups = MediaListSectionsMerger.OrderAndFilterGroups(
            groups,
            AppSettings.AnimeSectionOrder,
            AppSettings.DisplayAdultContent);

        foreach (var group in orderedGroups)
        {
            // First section and Rewatching default to expanded; all others default to collapsed.
            var defaultExpanded = sections.Count == 0 || group.Name == "Rewatching";
            var section = CreateSection(group.Name, defaultExpanded, expandedStates);
            section.AddItems(group.Entries);
            section.ApplySort(sortField, sortAscending);

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                section.ApplyFilter(filterText);
            }

            sections.Add(section);
        }

        return sections;
    }

    private static MediaListSection CreateSection(
        string title,
        bool defaultExpanded,
        IReadOnlyDictionary<string, bool> expandedStates)
    {
        if (expandedStates.TryGetValue(title, out var expanded))
        {
            return new MediaListSection(title, expanded);
        }

        return new MediaListSection(title, defaultExpanded);
    }
}
