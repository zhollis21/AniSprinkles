using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

/// <summary>
/// Discover page (issues #15/#16): public browse sections + search, no auth required.
/// Singleton, like the other flyout page models — the section data doubles as the TTL cache:
/// Discover data changes slowly, so revisits within <see cref="SectionsTtl"/> skip the network
/// entirely and pull-to-refresh is the explicit bypass. A flipped adult-content toggle also
/// invalidates the cache, since it changes both the query filters and which sections exist.
/// </summary>
public partial class DiscoverPageModel : ObservableObject
{
    private static readonly TimeSpan SectionsTtl = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(600);
    private const int SectionPerPage = 20;
    private const int SearchPerPage = 20;

    /// <summary>Below this many trimmed characters no search fires (rate-limit caution + junk-match avoidance).</summary>
    private const int SearchMinLength = 2;

    private readonly IAniListClient _aniListClient;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IUserFeedback _feedback;
    private readonly ErrorReportService _errorReportService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DiscoverPageModel> _logger;
    private readonly EntryActionCoordinator _entryActions;
    private readonly ListOperationRunner _listOps;

    private bool _hasLoaded;
    private DateTimeOffset _lastSuccessfulLoadUtc;
    private bool _loadedWithAdultContent;
    private bool _loadedAuthenticated;

    // Search debounce state: a new keystroke cancels the pending (or in-flight) search.
    private CancellationTokenSource? _searchDebounceCts;
    private string _activeSearchQuery = string.Empty;

    public DiscoverPageModel(
        IAniListClient aniListClient,
        IAuthService authService,
        INavigationService navigationService,
        IUserFeedback feedback,
        ErrorReportService errorReportService,
        TimeProvider timeProvider,
        ILogger<DiscoverPageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _navigationService = navigationService;
        _feedback = feedback;
        _errorReportService = errorReportService;
        _timeProvider = timeProvider;
        _logger = logger;
        _listOps = new ListOperationRunner(logger, feedback);

        // One row per section definition, in definition (= on-page) order. The 18+ rows always
        // exist but stay empty (hidden) unless the adult toggle is on. Each row pages itself
        // through BrowseAnime after being seeded by the single aliased Discover request.
        Rows = DiscoverSectionDefinitions.All
            .Select(definition => new DiscoverRow(
                definition,
                (page, _, ct) => DiscoverSectionFetch.PageAsync(
                    _aniListClient, _timeProvider, definition, page, SectionPerPage, ct)))
            .ToList();

        // Shared long-press flows. A successful mutation is written back onto EVERY item showing
        // that media (the same anime can sit in several rows plus the search results), keeping the
        // chips and the TTL-cached sections consistent without a refetch.
        _entryActions = new EntryActionCoordinator(aniListClient, errorReportService, logger, new EntryActionHost
        {
            OpenDetailsAsync = entry => NavigateToMediaByIdAsync(entry.MediaId),
            OnEntrySavedInPlaceAsync = entry => { ApplyEntryToItems(entry); return Task.CompletedTask; },
            OnEntryStatusChangedAsync = entry => { ApplyEntryToItems(entry); return Task.CompletedTask; },
            OnEntryRemovedAsync = entry => { ClearEntryFromItems(entry.MediaId); return Task.CompletedTask; },
            SetErrorDetails = details => ErrorDetails = details,
        });

        // Search results pagination: dedup + generation guarding live in the section; each new
        // query re-Seeds it (the generation bump supersedes any in-flight Load More).
        SearchSection = new PaginatedSection<BrowseMediaItem>(
            "SEARCH_MATCH",
            (page, _, ct) => _aniListClient.SearchAnimePageAsync(_activeSearchQuery, AdultFilter, page, SearchPerPage, ct),
            item => item.Node?.Id ?? 0);
        SearchSection.Changed += () =>
        {
            OnPropertyChanged(nameof(SearchIsLoadingMore));
            LoadMoreSearchResultsCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>false = SFW only; null omits the filter (adult toggle on), letting 18+ titles match.</summary>
    private static bool? AdultFilter => AppSettings.DisplayAdultContent ? null : false;

    // ── Main page state ──────────────────────────────────────────────
    // Discover is public, so unlike My Anime there are no auth states:
    //   InitialLoading → Content | Error
    //   Content        → Content (refresh keeps state) | Error (never: refresh failures keep Content)
    //   Error          → InitialLoading (retry)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.InitialLoading;

    // StateContainer.CurrentState is typed as string; null/empty restores default children.
    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _title = "Discover";

    /// <summary>True when the singleton already holds sections a new page instance can show immediately.</summary>
    public bool HasLoadedData => _hasLoaded && AnySectionHasItems;

    private bool AnySectionHasItems => Rows.Any(row => row.HasItems);

    // ── Sections ─────────────────────────────────────────────────────

    /// <summary>One row per <see cref="DiscoverSectionDefinitions"/> entry, in on-page order.
    /// Fixed for the page model's life; rows hide themselves when empty.</summary>
    public IReadOnlyList<DiscoverRow> Rows { get; }

    /// <summary>Horizontal Load More for one row (the carousel's scroll threshold command).</summary>
    [RelayCommand]
    private Task LoadMoreSection(string? sectionKey)
    {
        if (!Enum.TryParse<DiscoverSection>(sectionKey, out var section))
        {
            return Task.CompletedTask;
        }

        var row = Rows.FirstOrDefault(r => r.Definition.Section == section);
        if (row is null || !row.CanLoadMore)
        {
            return Task.CompletedTask;
        }

        return _listOps.RunAsync(
            $"Discover {section} · Load More",
            "discover",
            (int)section,
            () => row.LoadMoreAsync(),
            () => row.Items.Count);
    }

    // ── Search ───────────────────────────────────────────────────────

    /// <summary>Search results with infinite scroll; re-seeded per query.</summary>
    public PaginatedSection<BrowseMediaItem> SearchSection { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Toolbar-icon toggle, mirroring My Anime's search reveal: shows/hides the search bar;
    /// hiding clears the query, which restores the rows.</summary>
    [ObservableProperty]
    private bool _isSearchVisible;

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible)
        {
            SearchText = string.Empty;
        }
    }

    /// <summary>True while a query of <see cref="SearchMinLength"/>+ characters is entered — swaps the
    /// section rows for the results list. The rows stay alive underneath, so clearing the search
    /// restores them instantly with zero refetch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreSectionsVisible))]
    private bool _isSearchActive;

    public bool AreSectionsVisible => !IsSearchActive;

    /// <summary>True from first qualifying keystroke until the winning (non-superseded) fetch settles.</summary>
    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasNoSearchResults;

    public bool SearchIsLoadingMore => SearchSection.IsLoadingMore;

    partial void OnSearchTextChanged(string value)
    {
        // Cancel + dispose any pending debounce timer and any in-flight fetch it started, so the
        // per-keystroke CTSs (and their Task.Delay registrations) don't accumulate over a session.
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;

        var query = value?.Trim() ?? string.Empty;
        if (query.Length < SearchMinLength)
        {
            _activeSearchQuery = string.Empty;
            IsSearchActive = false;
            IsSearching = false;
            HasNoSearchResults = false;
            SearchSection.Reset();
            return;
        }

        IsSearchActive = true;
        IsSearching = true;
        HasNoSearchResults = false; // a previous query's empty state must not show under the new fetch
        _searchDebounceCts = new CancellationTokenSource();
        _ = DebouncedSearchAsync(query, _searchDebounceCts.Token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceDelay, token);
        }
        catch (TaskCanceledException)
        {
            // Another keystroke came in — this search is superseded.
            return;
        }

        _activeSearchQuery = query;
        _logger.LogInformation("Discover search firing for \"{Query}\"", query);

        try
        {
            var (items, pageInfo) = await _aniListClient.SearchAnimePageAsync(
                query, AdultFilter, page: 1, SearchPerPage, token);
            if (token.IsCancellationRequested || !string.Equals(_activeSearchQuery, query, StringComparison.Ordinal))
            {
                return; // superseded while the fetch was in flight
            }

            SearchSection.Seed(items, pageInfo);
            HasNoSearchResults = items.Count == 0;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!string.Equals(_activeSearchQuery, query, StringComparison.Ordinal))
            {
                return;
            }

            // Keep whatever results are showing; the snackbar is the failure signal.
            var apiEx = ex as AniListApiException;
            await _feedback.ShowSnackbarAsync(apiEx?.UserTitle ?? "Search failed. Please try again.");
            _errorReportService.Record(ex, "Discover search");
        }
        finally
        {
            // Only the still-active, non-superseded query owns the spinner. The token check matters:
            // a cancelled search can reach here BEFORE its successor's debounce sets _activeSearchQuery,
            // and must not clear the spinner the successor's keystroke just turned on.
            if (!token.IsCancellationRequested && string.Equals(_activeSearchQuery, query, StringComparison.Ordinal))
            {
                IsSearching = false;
            }
        }
    }

    [RelayCommand]
    private Task LoadMoreSearchResults()
        // Via ListOperationRunner like the section rows: PaginatedSection.LoadMoreAsync only
        // swallows cancellation, so a network/API failure here would otherwise propagate out of
        // the CollectionView's threshold command and crash. The runner swallows + snackbars it.
        => SearchSection.CanLoadMore
            ? _listOps.RunAsync(
                "Discover Search · Load More",
                "discover-search",
                0,
                () => SearchSection.LoadMoreAsync(),
                () => SearchSection.Items.Count)
            : Task.CompletedTask;

    // ── Error state ──────────────────────────────────────────────────

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorSubtitle = string.Empty;

    [ObservableProperty]
    private string _errorIconGlyph = string.Empty;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    // ── Loading ──────────────────────────────────────────────────────

    public async Task LoadAsync(bool forceReload = false)
    {
        if (IsBusy)
        {
            return;
        }

        var displayAdult = AppSettings.DisplayAdultContent;
        var now = _timeProvider.GetUtcNow();

        // Two things invalidate the cached, viewer-relative section data AND any active search:
        //  - an auth flip: mediaListEntry chips ride the results, so they must be refetched.
        //  - an adult-toggle flip: the search's AdultFilter changes, and stale results fetched
        //    under the old filter (e.g. 18+ covers after turning the toggle OFF) must not linger
        //    until the user edits the query.
        var isAuthenticated = !string.IsNullOrWhiteSpace(await _authService.GetAccessTokenAsync());
        var authChanged = _hasLoaded && isAuthenticated != _loadedAuthenticated;
        var adultChanged = _hasLoaded && _loadedWithAdultContent != displayAdult;
        if (authChanged || adultChanged)
        {
            _logger.LogInformation(
                "Discover cache invalidated (authChanged={AuthChanged}, adultChanged={AdultChanged}).",
                authChanged, adultChanged);
            forceReload = true;
            IsSearchVisible = false;
            if (!string.IsNullOrEmpty(SearchText))
            {
                SearchText = string.Empty; // OnSearchTextChanged resets the search state
            }
            else
            {
                SearchSection.Reset(); // empty SearchText leaves no keystroke to trigger the reset
            }
        }

        if (!forceReload
            && HasLoadedData
            && _loadedWithAdultContent == displayAdult
            && now - _lastSuccessfulLoadUtc < SectionsTtl)
        {
            _logger.LogDebug(
                "Discover sections cache hit (age {AgeMinutes:F1}m < TTL) — skipping fetch.",
                (now - _lastSuccessfulLoadUtc).TotalMinutes);
            CurrentState = PageState.Content;
            return;
        }

        IsBusy = true;
        if (!HasLoadedData)
        {
            CurrentState = PageState.InitialLoading;
        }

        try
        {
            var localNow = _timeProvider.GetLocalNow();
            var (currentSeason, currentYear) = AniListSeason.Current(localNow);
            var (nextSeason, nextYear) = AniListSeason.Next(localNow);

            var sections = await _aniListClient.GetDiscoverSectionsAsync(
                currentSeason, currentYear, nextSeason, nextYear,
                filterAdult: !displayAdult,
                includeAdultSections: displayAdult,
                SectionPerPage);

            // Re-seeding resets each row to page 1 and supersedes any in-flight row Load More.
            foreach (var row in Rows)
            {
                row.Seed(SectionPageFor(sections, row.Definition.Section));
            }

            _hasLoaded = true;
            _lastSuccessfulLoadUtc = _timeProvider.GetUtcNow();
            _loadedWithAdultContent = displayAdult;
            _loadedAuthenticated = isAuthenticated;
            CurrentState = PageState.Content;
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;

            if (HasLoadedData)
            {
                // Stale rows beat a blank page; pull-to-refresh is the retry path.
                await _feedback.ShowSnackbarAsync(apiEx?.UserTitle ?? "Refresh failed. Showing cached sections.");
                CurrentState = PageState.Content;
            }
            else
            {
                ErrorTitle = apiEx?.UserTitle ?? "Something Went Wrong";
                ErrorSubtitle = apiEx?.UserSubtitle ?? "An unexpected error occurred. Try again or check back later.";
                ErrorIconGlyph = apiEx?.IconGlyph ?? FluentIconsRegular.ErrorCircle24;
                CurrentState = PageState.Error;
            }

            _errorReportService.Record(ex, "Load Discover sections");
            ErrorDetails = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static DiscoverSectionPage SectionPageFor(DiscoverSections sections, DiscoverSection section) => section switch
    {
        DiscoverSection.Airing => sections.Airing,
        DiscoverSection.Trending => sections.Trending,
        DiscoverSection.Top => sections.Top,
        DiscoverSection.TopMovies => sections.TopMovies,
        DiscoverSection.AllTimePopular => sections.AllTimePopular,
        DiscoverSection.Upcoming => sections.Upcoming,
        DiscoverSection.PopularAdult => sections.PopularAdult,
        DiscoverSection.TopRatedAdult => sections.TopRatedAdult,
        _ => DiscoverSectionPage.Empty,
    };

    partial void OnCurrentStateChanged(PageState oldValue, PageState newValue)
        => _logger.LogInformation("PageState: {OldState} → {NewState} (key={StateKey})", oldValue, newValue, CurrentStateKey ?? "(null)");

    // ── Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private Task Refresh() => LoadAsync(forceReload: true);

    [RelayCommand]
    private Task RetryLoad() => LoadAsync(forceReload: true);

    [RelayCommand]
    private async Task NavigateToMedia(BrowseMediaItem? item)
    {
        // A long-press release still triggers the card's tap recognizer — swallow it so the
        // action sheet doesn't get a navigation underneath.
        if (Views.CollectionViewLongPress.ShouldSuppressTap())
        {
            return;
        }

        var media = item?.Node;
        var mediaId = media?.Id ?? 0;
        _logger.LogInformation("NAVTRACE Discover NavigateToMedia called with mediaId={MediaId}", mediaId);
        if (mediaId <= 0)
        {
            return;
        }

        // Same guard as the details-page carousels: Media Details queries type: ANIME, so a
        // non-anime id (possible in search results) would 404.
        if (media is { IsAnime: false })
        {
            await _feedback.ShowToastAsync("Manga & Novel details aren't supported yet.");
            return;
        }

        await NavigateToMediaByIdAsync(mediaId);
    }

    private Task NavigateToMediaByIdAsync(int mediaId)
        => _navigationService.GoToAsync("media-details", animate: false, new Dictionary<string, object>
        {
            ["mediaId"] = mediaId,
        });

    /// <summary>Long-press entry point: full menu for on-list media, Add to list otherwise.</summary>
    [RelayCommand]
    private async Task ShowItemActions(BrowseMediaItem? item)
    {
        if (item?.Node is null)
        {
            return;
        }

        var token = await _authService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            await _feedback.ShowToastAsync("Sign in to manage your list.");
            return;
        }

        var entry = item.ToListEntry();
        if (item.Node.ListStatus is null)
        {
            await _entryActions.ShowAddToListAsync(entry);
        }
        else
        {
            await _entryActions.ShowEntryMenuAsync(entry);
        }
    }

    private void ApplyEntryToItems(MediaListEntry entry)
    {
        foreach (var item in AllItems().Where(i => i.Node?.Id == entry.MediaId))
        {
            item.ApplyListEntry(entry);
        }
    }

    private void ClearEntryFromItems(int mediaId)
    {
        foreach (var item in AllItems().Where(i => i.Node?.Id == mediaId))
        {
            item.ClearListEntry();
        }
    }

    private IEnumerable<BrowseMediaItem> AllItems() =>
        Rows.SelectMany(row => row.Items).Concat(SearchSection.Items);

    [RelayCommand]
    private async Task ViewAll(string? sectionKey)
    {
        _logger.LogInformation("NAVTRACE Discover ViewAll called for section={Section}", sectionKey);
        if (string.IsNullOrEmpty(sectionKey))
        {
            return;
        }

        await _navigationService.GoToAsync("media-browse", animate: false, new Dictionary<string, object>
        {
            ["section"] = sectionKey,
        });
    }
}
