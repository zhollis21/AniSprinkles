using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;
using IconFont.Maui.FluentIcons;

namespace AniSprinkles.PageModels;

public partial class MyAnimePageModel : ObservableObject
{
    private static readonly TimeSpan ListRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IncrementDebounceDelay = TimeSpan.FromMilliseconds(1500);
    private const string DetailsRoute = "media-details";

    private readonly IAniListClient _aniListClient;
    private readonly IAuthService _authService;
    private readonly ErrorReportService _errorReportService;
    private readonly ILogger<MyAnimePageModel> _logger;
    private bool _hasLoaded;
    private DateTimeOffset _lastSuccessfulLoadUtc;

    // +1 debounce state: rapid taps batch into a single API call.
    private CancellationTokenSource? _incrementDebounceCts;
    private MediaListEntry? _pendingIncrementEntry;
    private int? _preIncrementProgress;
    private bool _isCompletionFlowActive;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoginPrompt))]
    private bool _isAuthenticationPending = true;

    public bool ShowLoginPrompt => !IsAuthenticated && !IsAuthenticationPending;

    // Set by the page code-behind during the fast-path deferred rebuild to
    // keep a spinner visible while a new content-view instance is being created.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInitialLoading))]
    private bool _isRebuilding;

    // Keep list loading UX aligned with details page: a single centered first-load indicator.
    // Also true while IsRebuilding so the fast-path return trip shows a spinner
    // instead of a blank page during the Shell-transition deferred delay.
    public bool IsInitialLoading => (IsBusy && IsAuthenticated && Sections.Count == 0) || IsRebuilding;

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
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    [ObservableProperty]
    private bool _hasErrorDetails;

    [ObservableProperty]
    private bool _isErrorDetailsVisible;

    // ── Error state (full-page error view) ──────────────────────────
    [ObservableProperty]
    private bool _isErrorState;

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

    public MyAnimePageModel(IAniListClient aniListClient, IAuthService authService, ErrorReportService errorReportService, ILogger<MyAnimePageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _errorReportService = errorReportService;
        _logger = logger;
    }

    public async Task LoadAsync(bool forceReload = false)
    {
        if (IsBusy)
        {
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

            // Keep user context (expanded/collapsed groups) when data refreshes.
            var expandedStates = Sections.ToDictionary(
                section => section.Title,
                section => section.IsExpanded,
                StringComparer.Ordinal);

            _logger.LogInformation("Loading My Anime list.");
            SentrySdk.AddBreadcrumb("Load My Anime list", "navigation", "state");

            IsAuthenticated = isAuthenticated;
            IsAuthenticationPending = false;

            if (!IsAuthenticated)
            {
                ErrorDetails = string.Empty;
                IsErrorDetailsVisible = false;
                Sections = [];
                _hasLoaded = true;
                _lastSuccessfulLoadUtc = default;
                return;
            }

            Title = "My Anime";
            StatusMessage = string.Empty;
            ErrorDetails = string.Empty;
            IsErrorDetailsVisible = false;
            IsErrorState = false;

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
            // Capture current sort/filter state for off-thread section building.
            var sortField = CurrentSortField;
            var sortAsc = SortAscending;
            var filterText = SearchText;
            // Grouping can be heavy on large lists; build sections off the UI thread.
            var sections = await Task.Run(() => BuildSections(groups, expandedStates, sortField, sortAsc, filterText));
            Sections = sections;
            OnPropertyChanged(nameof(HasNoResults));
            _hasLoaded = true;
            _lastSuccessfulLoadUtc = DateTimeOffset.UtcNow;

            // Cache RELEASING media IDs for the background airing notification worker.
            CacheReleasingMediaIds(groups);
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;

            if (hadExistingSections && IsAuthenticated)
            {
                // Prefer stale data over blank UI when refresh fails after a previously successful load.
                StatusMessage = apiEx?.UserTitle ?? "Refresh failed. Showing cached list.";
                IsErrorState = false;
                _hasLoaded = true;
            }
            else
            {
                // Full-page error state — no cached data to fall back on.
                ErrorTitle = apiEx?.UserTitle ?? "Something Went Wrong";
                ErrorSubtitle = apiEx?.UserSubtitle ?? "An unexpected error occurred. Try again or check back later.";
                ErrorIconGlyph = apiEx?.IconGlyph ?? IconFont.Maui.FluentIcons.FluentIconsRegular.ErrorCircle24;
                IsErrorState = true;
                StatusMessage = string.Empty;
                Sections = [];
                _hasLoaded = false;
            }

            _errorReportService.Record(ex, "Load My Anime");
            ErrorDetails = ex.Message;
            IsErrorDetailsVisible = false;
        }
        finally
        {
            IsBusy = false;
            IsAuthenticationPending = false;
        }
    }

    // ── Property change handlers ─────────────────────────────────────

    partial void OnStatusMessageChanged(string value)
        => HasStatusMessage = !string.IsNullOrWhiteSpace(value);

    partial void OnErrorDetailsChanged(string value)
        => HasErrorDetails = !string.IsNullOrWhiteSpace(value);

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsInitialLoading));

    partial void OnIsAuthenticatedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInitialLoading));
        OnPropertyChanged(nameof(ShowLoginPrompt));
    }

    partial void OnSectionsChanged(ObservableCollection<MediaListSection> value)
        => OnPropertyChanged(nameof(IsInitialLoading));

    partial void OnSearchTextChanged(string value)
    {
        foreach (var section in Sections)
        {
            section.ApplyFilter(value);
        }

        OnPropertyChanged(nameof(HasNoResults));
    }

    partial void OnCurrentSortFieldChanged(SortField value)
        => ApplySortToAllSections();

    partial void OnSortAscendingChanged(bool value)
        => ApplySortToAllSections();

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

        var newProgress = (entry.Progress ?? 0) + 1;
        var totalEpisodes = entry.MaxEpisodes;

        // ── Completion flow: no debounce, save immediately ──────────
        if (totalEpisodes.HasValue && newProgress >= totalEpisodes.Value)
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

                newProgress = totalEpisodes.Value;
                var markCompleted = await ShowCompletionPopupAsync(
                    entry.Media.DisplayTitle, totalEpisodes.Value);

                if (markCompleted)
                {
                    entry.Progress = newProgress;
                    entry.Status = MediaListStatus.Completed;

                    var score = await PromptForScoreAsync(entry.Media.DisplayTitle);
                    if (score.HasValue)
                    {
                        entry.Score = score.Value;
                    }

                    try
                    {
                        await _aniListClient.SaveMediaListEntryAsync(entry);
                        await LoadAsync(forceReload: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to save completed entry for media {MediaId}", entry.MediaId);
                        StatusMessage = "Failed to save. Please try again.";
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
        }
        catch (Exception ex)
        {
            // Revert to the progress before the entire debounce series.
            if (originalProgress.HasValue)
            {
                entry.Progress = originalProgress.Value;
            }

            _logger.LogError(ex, "Failed to save progress for media {MediaId}, reverted to {Progress}", entry.MediaId, originalProgress);
            StatusMessage = "Failed to save. Please try again.";
            ErrorDetails = _errorReportService.Record(ex, "Increment progress");
        }
    }

    private static readonly PopupOptions TransparentPopupOptions = new()
    {
        Shape = null,
        Shadow = null,
        CanBeDismissedByTappingOutsideOfPopup = false,
    };

    private static async Task<bool> ShowCompletionPopupAsync(string animeTitle, int totalEpisodes)
    {
        var popup = new Views.CompletionPopup(animeTitle, totalEpisodes);
        var result = await Shell.Current.CurrentPage.ShowPopupAsync<bool>(popup, TransparentPopupOptions, CancellationToken.None);
        return !result.WasDismissedByTappingOutsideOfPopup && result.Result;
    }

    private static async Task<double?> PromptForScoreAsync(string? animeTitle = null)
    {
        var popup = new Views.RatingPopup(animeTitle);
        var result = await Shell.Current.CurrentPage.ShowPopupAsync<object>(popup, TransparentPopupOptions, CancellationToken.None);
        if (result.WasDismissedByTappingOutsideOfPopup)
        {
            return null;
        }

        return result.Result as double?;
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
        var confirmed = await Shell.Current.CurrentPage.DisplayAlertAsync(
            "Remove from List",
            $"Remove {title} from your list?",
            "Remove",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

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
            StatusMessage = "Failed to remove. Please try again.";
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

        // Apply side effects based on target status.
        switch (targetStatus)
        {
            case MediaListStatus.Completed:
                entry.Status = MediaListStatus.Completed;
                if (entry.MaxEpisodes.HasValue)
                {
                    entry.Progress = entry.MaxEpisodes.Value;
                }

                var score = await PromptForScoreAsync(title);
                if (score.HasValue)
                {
                    entry.Score = score.Value;
                }

                break;

            case MediaListStatus.Repeating:
                entry.Status = MediaListStatus.Repeating;
                entry.Progress = 0;
                entry.Repeat = (entry.Repeat ?? 0) + 1;
                break;

            default:
                entry.Status = targetStatus;
                break;
        }

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
            StatusMessage = "Failed to move. Please try again.";
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

    private static async Task ShowToastAsync(string message)
    {
        var toast = Toast.Make(message, ToastDuration.Short);
        await toast.Show();
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
        IsErrorState = false;
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
                StatusMessage = "Sign in canceled.";
                SentrySdk.AddBreadcrumb("Sign-in canceled", "auth", "user");
                return;
            }

            SentrySdk.AddBreadcrumb("Sign-in successful", "auth", "user");
        }
        catch (Exception ex)
        {
            StatusMessage = "Sign in failed. Tap Details for more.";
            ErrorDetails = _errorReportService.Record(ex, "Sign in");
            IsErrorDetailsVisible = false;
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
            StatusMessage = "Unable to open details.";
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
            await Shell.Current.GoToAsync(DetailsRoute, animate: false, new Dictionary<string, object>
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

    // ── Airing notification cache ─────────────────────────────────────

    /// <summary>
    /// Saves the media IDs of currently-airing ("RELEASING") anime from the user's
    /// Watching and Planning lists to Preferences so the background <c>AiringCheckWorker</c>
    /// can poll AniList's AiringSchedule API without fetching the full list.
    /// Planning is included so users are notified when a show they intend to watch airs.
    /// </summary>
    private static void CacheReleasingMediaIds(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups)
    {
        var releasingIds = groups
            .Where(g => g.Name is "Watching" or "Rewatching" or "Planning")
            .SelectMany(g => g.Entries)
            .Where(e => e.Media?.Status is "RELEASING")
            .Select(e => e.MediaId)
            .Distinct()
            .ToList();

        Preferences.Default.Set("airing_media_ids", string.Join(",", releasingIds));
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

        // Sort groups by user's custom section order from AniList settings.
        var sectionOrder = AppSettings.AnimeSectionOrder;
        var orderedGroups = sectionOrder.Count > 0
            ? groups.OrderBy(g =>
            {
                var idx = sectionOrder.IndexOf(g.Name);
                return idx >= 0 ? idx : int.MaxValue;
            }).ToList()
            : groups;

        foreach (var group in orderedGroups)
        {
            IEnumerable<MediaListEntry> entries = group.Entries;

            // Filter adult content if the user has it disabled.
            if (!AppSettings.DisplayAdultContent)
            {
                entries = entries.Where(e => e.Media?.IsAdult != true);
            }

            var list = entries.ToList();
            if (list.Count == 0)
            {
                continue;
            }

            // First section defaults to expanded; others default to collapsed.
            var defaultExpanded = sections.Count == 0;
            var section = CreateSection(group.Name, defaultExpanded, expandedStates);
            section.AddItems(list);
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

    // ── Details / Error commands ─────────────────────────────────────

    [RelayCommand]
    private void ToggleDetails()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        IsErrorDetailsVisible = !IsErrorDetailsVisible;
    }

    [RelayCommand]
    private async Task CopyError()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(ErrorDetails);
        StatusMessage = "Error details copied.";
    }

    [RelayCommand]
    private async Task ShareError()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = ErrorDetails,
            Title = "AniSprinkles Error Details"
        });
    }
}
