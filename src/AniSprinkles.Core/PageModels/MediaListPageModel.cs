using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

public partial class MediaListPageModel : ObservableObject
{
    /// <summary>Which half of the Library tab this instance serves (#12).</summary>
    public MediaKind Kind { get; }

    private static readonly TimeSpan ListRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IncrementDebounceDelay = TimeSpan.FromMilliseconds(1500);
    private const string DetailsRoute = "media-details";

    // ── Persisted UI preferences (device-scoped, not cleared on sign-out) ──
    // View mode lives in ListViewModePreference — shared with the media-browse (View All) lists.
    // Prefixed per type so sorting your manga by title does not reorder your anime. View mode is
    // deliberately NOT prefixed — ListViewModePreference is app-wide and shared with the
    // media-browse lists, so carving out an exception here would make Library the odd one out.
    private string SortFieldPreferenceKey => Kind == MediaKind.Manga ? "manga_sort_field" : "anime_sort_field";
    private string SortAscendingPreferenceKey => Kind == MediaKind.Manga ? "manga_sort_ascending" : "anime_sort_ascending";

    /// <summary>The section order for this type, as the viewer set it on AniList.</summary>
    private IReadOnlyList<string> SectionOrder =>
        Kind == MediaKind.Manga ? AppSettings.MangaSectionOrder : AppSettings.AnimeSectionOrder;

    private readonly IAniListClient _aniListClient;
    private readonly IAuthService _authService;
    protected readonly IAiringNotificationService AiringNotifications;
    private readonly ErrorReportService _errorReportService;
    private readonly IPreferences _preferences;
    private readonly INavigationService _navigationService;
    private readonly IUserFeedback _feedback;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MediaListPageModel> _logger;
    private readonly EntryActionCoordinator _entryActions;
    private bool _hasLoaded;
    private DateTimeOffset _lastSuccessfulLoadUtc;

    /// <summary>
    /// The DisplayAdultContent value the current <see cref="Sections"/> were built under (#118).
    /// The adult filter is applied while building, so a change only takes effect when a load
    /// actually runs — which the freshness window below would otherwise suppress for five minutes.
    /// Discover, Search and View All each keep the same comparison against their own results.
    /// </summary>
    private bool _loadedWithAdultContent;

    /// <summary>
    /// The display settings the current <see cref="Sections"/> were rendered under (#127).
    /// <para>
    /// Its counterpart above forces a <em>refetch</em>, because the adult filter decides which
    /// entries exist. These decide how the entries already in hand render, so the answer is a
    /// re-projection instead — spending an AniList request to change a title's language would be the
    /// wrong trade under the rate-limit budget.
    /// </para>
    /// </summary>
    private DisplaySettingsSnapshot _renderedDisplaySettings = DisplaySettingsSnapshot.Current;

    // +1 debounce state: rapid taps batch into a single API call.
    private CancellationTokenSource? _incrementDebounceCts;
    private MediaListEntry? _pendingIncrementEntry;
    private int? _preIncrementProgress;

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
    private string _title = "Library";

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
    [NotifyPropertyChangedFor(nameof(SelectedSortCode))]
    private SortField _currentSortField = SortField.LastUpdated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSortCode))]
    [NotifyPropertyChangedFor(nameof(SortIconGlyph))]
    private bool _sortAscending;

    /// <summary>
    /// Glyph for the top-bar sort button, mirroring the active direction as the picker rows read:
    /// down-lines for ascending (A→Z, low→high — first/smallest on top), up-lines for descending.
    /// </summary>
    public string SortIconGlyph => SortAscending
        ? Glyphs.Regular.ArrowSortDownLines24
        : Glyphs.Regular.ArrowSortUpLines24;

    /// <summary>
    /// Rows for the shared sort picker, which either half of the Library tab opens from its
    /// top-bar sort icon.
    /// Each <see cref="SortOption.Code"/> encodes field + direction as "Field:dir" so one tap fully
    /// specifies the sort; <see cref="SelectSort"/> parses it back into <see cref="CurrentSortField"/> +
    /// <see cref="SortAscending"/>. Built from <see cref="MediaListSortDefinitions"/> (pure, unit-tested data)
    /// so the codes are validated at build time and each instance gets its own mutable IsSelected state.
    /// </summary>
    public IReadOnlyList<SortOption> SortOptions { get; } =
        MediaListSortDefinitions.All
            .Select(d => new SortOption { Code = d.Code, Display = d.Display })
            .ToList();

    /// <summary>The active sort as a "Field:dir" code, matching one <see cref="SortOptions"/> entry.</summary>
    public string SelectedSortCode => $"{CurrentSortField}:{(SortAscending ? "asc" : "desc")}";

    // ── Empty state (#12) ───────────────────────────────
    // Worded per type. The manga half meets this far more often than the anime one, since plenty
    // of AniList accounts track anime only and arrive here the first time they swipe across.

    public string EmptyTitle => Kind == MediaKind.Manga
        ? "No manga yet"
        : "No anime yet";

    public string EmptySubtitle => Kind == MediaKind.Manga
        ? "Manga you add on AniList will show up here. Try the Search tab to find something."
        : "Anime you add on AniList will show up here. Try the Discover or Search tabs.";

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
        ListViewMode.Large => Glyphs.Regular.Grid24,
        ListViewMode.Compact => Glyphs.Regular.TextBulletListSquare24,
        _ => Glyphs.Regular.List24,
    };

    protected MediaListPageModel(MediaKind kind, IAniListClient aniListClient, IAuthService authService, IAiringNotificationService airingNotificationService, ErrorReportService errorReportService, IPreferences preferences, INavigationService navigationService, IDialogService dialogs, IUserFeedback feedback, ListEntryStatusFlow statusFlow, TimeProvider timeProvider, ILogger<MediaListPageModel> logger)
    {
        Kind = kind;
        _aniListClient = aniListClient;
        _authService = authService;
        AiringNotifications = airingNotificationService;
        _errorReportService = errorReportService;
        _preferences = preferences;
        _navigationService = navigationService;
        _feedback = feedback;
        _timeProvider = timeProvider;
        _logger = logger;

        // Long-press flows live in the shared coordinator (also used by Discover/browse);
        // the hooks keep this page's section bookkeeping and +1-debounce semantics intact.
        _entryActions = new EntryActionCoordinator(aniListClient, errorReportService, dialogs, feedback, statusFlow, logger, new EntryActionHost
        {
            OpenDetailsAsync = entry => OpenDetails(entry),
            OnBeforeFlowAsync = FlushPendingIncrementAsync,
            OnOptimisticRemove = RemoveEntryFromCurrentSection,
            // In-place saves (rate, progress edit) need no reload — the entry's observable
            // properties refresh the card.
            OnEntryStatusChangedAsync = _ => LoadAsync(forceReload: true),
            OnEntryRemovedAsync = _ => LoadAsync(forceReload: true),
            OnMutationFailedAsync = () => LoadAsync(forceReload: true),
            SetErrorDetails = details => ErrorDetails = details,
        });

        // Restore persisted UI preferences directly into backing fields to avoid
        // triggering partial property-changed handlers before the object is fully constructed.
        _currentViewMode = ListViewModePreference.Load(preferences);

        var savedSort = preferences.Get(SortFieldPreferenceKey, nameof(SortField.LastUpdated));
        if (Enum.TryParse<SortField>(savedSort, out var restoredSort))
        {
            _currentSortField = restoredSort;
        }

        _sortAscending = preferences.Get(SortAscendingPreferenceKey, false);

        // Highlight the restored sort in the picker rows (the popup reads IsSelected at build time).
        SyncSortSelection();
    }

    public async Task LoadAsync(bool forceReload = false)
    {
        _logger.LogInformation(
            "MediaList[{Kind}] LoadAsync enter (forceReload={ForceReload}, isBusy={IsBusy}, hasLoaded={HasLoaded}, currentState={CurrentState}, hadSections={HadSections})",
            Kind, forceReload, IsBusy, _hasLoaded, CurrentState, Sections.Count);

        if (IsBusy)
        {
            _logger.LogInformation("MediaList[{Kind}] LoadAsync skipped: already busy.", Kind);
            return;
        }

        // Set IsBusy immediately — before any awaits — so concurrent callers
        // are rejected by the guard above. All cleanup happens in finally.
        IsBusy = true;
        var hadExistingSections = Sections.Count > 0;
        try
        {
            // Before the short-circuit below, not after: a display-setting change has to reach the
            // sections already on screen even when the freshness window skips the load entirely,
            // which is the case the user actually hits (#127). A full load re-snapshots at the end.
            ReprojectIfDisplaySettingsChanged();

            var token = await _authService.GetAccessTokenAsync();
            var isAuthenticated = !string.IsNullOrWhiteSpace(token);
            // OnAppearing can fire often; keep list navigation snappy by skipping refreshes inside a short stale window.
            var isFresh = _lastSuccessfulLoadUtc != default &&
                DateTimeOffset.UtcNow - _lastSuccessfulLoadUtc < ListRefreshInterval;

            // An adult-toggle flip has to defeat that window (#118). The filter is applied while
            // sections are built, so skipping the load leaves the 18+ entries in place — and unlike
            // the other browse surfaces, whose staleness clears on the next appearance, this window
            // is time-based, so tabbing away and back would not clear it either.
            var adultChanged = _loadedWithAdultContent != AppSettings.DisplayAdultContent;

            if (_hasLoaded && !forceReload && isAuthenticated == IsAuthenticated)
            {
                // Signed out has no sections to filter, so that branch returns either way.
                if (!isAuthenticated || (isFresh && !adultChanged))
                {
                    return;
                }
            }

            if (forceReload)
            {
                _lastSuccessfulLoadUtc = default;
            }

            _logger.LogInformation("Loading {Kind} list.", Kind);
            SentrySdk.AddBreadcrumb($"Load {Kind} list", "navigation", "state");

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

            Title = "Library";
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
            var groups = await _aniListClient.GetMediaListGroupedAsync(Kind);
            // Capture current sort/filter state for section building.
            var sortField = CurrentSortField;
            var sortAsc = SortAscending;
            var filterText = SearchText;

            if (Sections.Count == 0)
            {
                // Cold path — no existing sections to diff against. Build off-thread because grouping
                // can be heavy on large lists, then publish in one assignment.
                var expandedStates = new Dictionary<string, bool>(StringComparer.Ordinal);
                var sectionOrder = SectionOrder;
                var sections = await Task.Run(() => BuildSections(groups, expandedStates, sectionOrder, sortField, sortAsc, filterText));
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
                    SectionOrder,
                    AppSettings.DisplayAdultContent,
                    sortField,
                    sortAsc,
                    filterText);
                var mergeMs = Stopwatch.GetElapsedTime(mergeStart).TotalMilliseconds;
                _logger.LogDebug(
                    "MediaList[{Kind}] merge in {ElapsedMs:F1}ms: {SectionsAdded} sec+, {SectionsRemoved} sec-, {EntriesAdded} ent+, {EntriesRemoved} ent-, {EntriesMoved} moved, {EntriesUpdated} updated, {SectionsNeedingReset} reset",
                    Kind,
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

            // Read here rather than from the value captured at entry: SyncFromViewer above may have
            // moved it, and this has to describe the sections that were actually just built.
            _loadedWithAdultContent = AppSettings.DisplayAdultContent;

            // Same reasoning: SyncFromViewer may have moved these, and the sections were just built
            // from whatever they are now. Re-snapshotting here is what stops the next appearance
            // from re-projecting work this load already did.
            _renderedDisplaySettings = DisplaySettingsSnapshot.Current;

            // Zero sections after a SUCCESSFUL load is an empty list, not a failure — and it is a
            // real case rather than a defensive one, since plenty of AniList accounts track anime
            // only and would otherwise meet the manga tab as a blank page (#12). Note this reads
            // the built sections rather than the raw groups: the adult filter can empty a list that
            // came back non-empty, and that should land here too.
            CurrentState = Sections.Count == 0 ? PageState.Empty : PageState.Content;

            // Anime only: manga does not air, so the manga half overrides this to nothing rather
            // than caching ids a worker would poll an airing schedule for (#12).
            OnListLoaded(groups);
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;

            if (hadExistingSections && IsAuthenticated)
            {
                // Prefer stale data over blank UI when refresh fails after a previously successful load.
                // Pull-to-refresh is the retry path, so no action on the snackbar.
                await _feedback.ShowSnackbarAsync(apiEx?.UserTitle ?? "Refresh failed. Showing cached list.");
                CurrentState = PageState.Content;
                _hasLoaded = true;
            }
            else
            {
                // Full-page error state — no cached data to fall back on.
                ErrorTitle = apiEx?.UserTitle ?? "Something Went Wrong";
                ErrorSubtitle = apiEx?.UserSubtitle ?? "An unexpected error occurred. Try again or check back later.";
                ErrorIconGlyph = apiEx?.IconGlyph ?? Glyphs.Regular.ErrorCircle24;
                CurrentState = PageState.Error;
                Sections = [];
                _hasLoaded = false;
            }

            _errorReportService.Record(ex, $"Load {Kind} list");
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
        => ListViewModePreference.Save(_preferences, value);

    /// <summary>Re-reads the shared view-mode preference (it can change from media-browse pages).</summary>
    public void SyncViewModeFromPreference()
    {
        var stored = ListViewModePreference.Load(_preferences);
        if (stored != CurrentViewMode)
        {
            CurrentViewMode = stored; // the changed handler re-saves the same value — harmless
        }
    }

    // Set while SelectSort changes field + direction together, so the per-property handlers persist and
    // re-sync the highlight but skip their own re-sort; SelectSort applies one re-sort after both are set.
    private bool _suppressSectionSort;

    partial void OnCurrentSortFieldChanged(SortField value)
    {
        _preferences.Set(SortFieldPreferenceKey, value.ToString());
        SyncSortSelection();
        if (!_suppressSectionSort)
        {
            ApplySortToAllSections();
        }
    }

    partial void OnSortAscendingChanged(bool value)
    {
        _preferences.Set(SortAscendingPreferenceKey, value);
        SyncSortSelection();
        if (!_suppressSectionSort)
        {
            ApplySortToAllSections();
        }
    }

    /// <summary>Mark the picker row matching the active sort as selected (drives the popup highlight).</summary>
    private void SyncSortSelection()
    {
        foreach (var option in SortOptions)
        {
            option.IsSelected = string.Equals(option.Code, SelectedSortCode, StringComparison.Ordinal);
        }
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
    private void SelectSort(string? code)
    {
        // Code is "Field:dir" (e.g. "LastUpdated:desc") from the picker. Parse both halves; bail on anything
        // malformed or unchanged so we don't churn preferences / re-sort needlessly.
        if (string.IsNullOrEmpty(code) || string.Equals(code, SelectedSortCode, StringComparison.Ordinal))
        {
            return;
        }

        var parts = code.Split(':');
        if (parts.Length != 2 || !Enum.TryParse<SortField>(parts[0], out var field)
            || parts[1] is not ("asc" or "desc"))
        {
            // The picker only ever emits valid "Field:asc"/"Field:desc" codes, so a malformed one
            // is a wiring bug (e.g. a typo'd picker entry), not user state. Log at Error so it surfaces
            // as a Sentry issue (a "should never happen" tripwire), not just a silent no-op.
            _logger.LogError("Ignoring malformed sort code: {SortCode}", code);
            return;
        }

        var ascending = string.Equals(parts[1], "asc", StringComparison.Ordinal);

        // Set both halves, then re-sort once. The change handlers each persist + re-sync the highlight, but
        // _suppressSectionSort stops them from re-sorting mid-update (which would sort every section twice —
        // once with the new field but stale direction). Apply the single, correct sort after both are set.
        _suppressSectionSort = true;
        try
        {
            CurrentSortField = field;
            SortAscending = ascending;
        }
        finally
        {
            _suppressSectionSort = false;
        }

        ApplySortToAllSections();
    }

    /// <summary>
    /// Re-renders the sections already on screen when a display setting moved under them (#127).
    /// No fetch, and nothing at all when the settings are unchanged — this runs on every appearance,
    /// and tab switching must stay free.
    /// </summary>
    private void ReprojectIfDisplaySettingsChanged()
    {
        var current = DisplaySettingsSnapshot.Current;
        if (current == _renderedDisplaySettings)
        {
            return;
        }

        if (current.RenderingDiffersFrom(_renderedDisplaySettings))
        {
            foreach (var entry in Sections.SelectMany(s => s.AllItems))
            {
                entry.RefreshDisplayProjections();
            }
        }

        // The Title sort orders by Media.DisplayTitle, so a language change moves rows as well as
        // re-rendering their text. Only that sort is affected — the others read data, not settings.
        if (CurrentSortField == SortField.Title && current.TitleLanguageDiffersFrom(_renderedDisplaySettings))
        {
            ApplySortToAllSections();
        }

        if (current.SectionOrderDiffersFrom(_renderedDisplaySettings))
        {
            MediaListSectionsMerger.ReorderSections(Sections, SectionOrder);
        }

        _renderedDisplaySettings = current;
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
                await _feedback.ShowToastAsync("You're caught up");
            }

            return;
        }

        var newProgress = (entry.Progress ?? 0) + 1;

        // ── Completion flow: only when we know the total. Shows confirm + rating
        // popups, then saves immediately (no debounce). Long-running airing shows
        // without a declared episode count fall through to the normal +1 path.
        if (entry.IsCompletionAt(newProgress))
        {
            await _entryActions.RunCompletionFlowAsync(entry);
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
        //
        // Reads and writes Progress directly rather than going through the active-unit helpers
        // (#12): Library is fed by MediaListCollection(type: ANIME), so every entry here counts
        // episodes. The manga list arrives with the rest of #12 and generalises this alongside it.
        if (_pendingIncrementEntry != entry)
        {
            _preIncrementProgress = entry.Progress;
            _pendingIncrementEntry = entry;
        }

        entry.Progress = newProgress;

        // Immediately tell the user they've caught up the moment the last available
        // +1 lands. This is instant (pre-debounce) so they see it before scrolling away.
        // Finite-total shows don't reach here — they go through the completion flow above.
        if (!entry.HasKnownProgressTotal && !entry.CanIncrementProgress)
        {
            await _feedback.ShowToastAsync("You're caught up!");
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
            // Through TimeProvider so the 1500 ms batching window is deterministic under test.
            await Task.Delay(IncrementDebounceDelay, _timeProvider, token);
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
            await _feedback.ShowToastAsync("Saved");
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
            await _feedback.ShowFailureSnackbarAsync(ex, "Failed to save. Please try again.", retryAction: null);
            ErrorDetails = _errorReportService.Record(ex, "Increment progress");
        }
    }

    // ── Long-press action menu ─────────────────────────────────────
    // The menu and all of its flows (edit progress, complete, rate, move, remove, and the popup +
    // persistence plumbing) live in the shared EntryActionCoordinator, so Discover/browse/search
    // surfaces behave identically. This page contributes only the host hooks wired in the ctor.

    [RelayCommand]
    private async Task ShowActionMenu(MediaListEntry? entry)
    {
        if (entry?.Media is null || entry.Status is null)
        {
            return;
        }

        await _entryActions.ShowEntryMenuAsync(entry);
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
                await _feedback.ShowToastAsync("Sign in canceled.");
                SentrySdk.AddBreadcrumb("Sign-in canceled", "auth", "user");
                return;
            }

            SentrySdk.AddBreadcrumb("Sign-in successful", "auth", "user");
        }
        catch (Exception ex)
        {
            await _feedback.ShowSnackbarAsync("Sign in failed. Tap Details for more.");
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
        OnSignedOut();
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
            await _feedback.ShowToastAsync("Unable to open details.");
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

    // Whether the Library permission prompt has already been shown — so a denial doesn't re-prompt
    // on every list load — is one of the keys AiringNotificationState owns (#141). It used to be a
    // private const here and a separate raw literal in the app project, on the far side of the test
    // boundary, where a rename to either would have gone unnoticed. Cleared on sign-out so a fresh
    // session gets the prompt.

    /// <summary>
    /// Called after the first successful authenticated list load. On API 33+ (where
    /// POST_NOTIFICATIONS requires a runtime dialog), shows the permission prompt once and
    /// syncs the result to AniList. On API &lt;33 (no runtime permission needed), respects the
    /// existing AniList value — schedules WorkManager if already enabled, does nothing otherwise.
    /// </summary>
    protected async Task RequestNotificationPermissionIfNeededAsync()
    {
        try
        {
            // Only prompt once from the Library tab. Settings can re-prompt via the explicit toggle.
            if (AiringNotificationState.HasPromptedForPermission(_preferences))
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
                        AiringNotifications.SchedulePeriodicCheck();
                    }
                }
                catch (Exception ex)
                {
                    // Don't set the prompted flag on failure — allow retry on next load.
                    _logger.LogWarning(ex, "Failed to check AniList airing notifications setting on API <33");
                    return;
                }

                AiringNotificationState.MarkPromptedForPermission(_preferences);
                return;
            }

            // Mark as prompted before awaiting the system dialog so concurrent/rapid loads
            // don't double-prompt. The permission dialog itself is a one-shot system UI —
            // even if the AniList sync afterward fails, the prompt already happened.
            AiringNotificationState.MarkPromptedForPermission(_preferences);

            bool granted = await AiringNotifications.RequestPermissionAsync();

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

            AiringNotifications.SchedulePeriodicCheck();
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
    /// <summary>
    /// Post-load side effects that belong to one type only. The anime half caches RELEASING media
    /// ids for the background airing worker and asks for notification permission on first load;
    /// the manga half has nothing to do here, because AniList publishes no chapter schedule.
    /// </summary>
    protected virtual void OnListLoaded(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups)
    {
    }

    /// <summary>Airing-notification teardown on sign-out. Anime-only, for the same reason.</summary>
    protected virtual void OnSignedOut()
    {
    }

    protected void CacheReleasingMediaIds(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups)
    {
        var releasingIds = groups
            .Where(g => g.Name is "Watching" or "Rewatching" or "Planning")
            .SelectMany(g => g.Entries)
            .Where(e => e.Media?.Status is "RELEASING")
            .Select(e => e.MediaId)
            .Distinct()
            .ToList();

        AiringNotificationState.WriteMediaIds(_preferences, releasingIds);
    }

    // ── Section building ─────────────────────────────────────────────

    private static ObservableCollection<MediaListSection> BuildSections(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups,
        IReadOnlyDictionary<string, bool> expandedStates,
        IReadOnlyList<string> sectionOrder,
        SortField sortField,
        bool sortAscending,
        string filterText)
    {
        var sections = new ObservableCollection<MediaListSection>();

        var orderedGroups = MediaListSectionsMerger.OrderAndFilterGroups(
            groups,
            sectionOrder,
            AppSettings.DisplayAdultContent);

        foreach (var group in orderedGroups)
        {
            // First section defaults to expanded, as does the re-consuming one — "Rewatching" for
            // anime, "Rereading" for manga. All others default to collapsed.
            var defaultExpanded = sections.Count == 0
                || group.Name is "Rewatching" or "Rereading";
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
