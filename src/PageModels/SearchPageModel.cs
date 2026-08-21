using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

/// <summary>
/// Search tab (issues #16/#43): global anime search, no auth required. Extracted from
/// <see cref="DiscoverPageModel"/> when search became its own tab — the debounce, generation
/// guarding and long-press write-back are the same logic that shipped on Discover.
///
/// Singleton, like the other tab page models, so the query and its results survive a tab
/// switch. That is deliberate here: coming back to the Search tab should show what you were
/// looking at, not a cleared box. An auth or adult-toggle flip does clear it, because both
/// change what the results should contain.
/// </summary>
public partial class SearchPageModel : ObservableObject
{
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(600);
    private const int SearchPerPage = 20;

    /// <summary>Below this many trimmed characters no search fires (rate-limit caution + junk-match avoidance).</summary>
    private const int SearchMinLength = 2;

    private readonly IAniListClient _aniListClient;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IUserFeedback _feedback;
    private readonly ErrorReportService _errorReportService;
    private readonly ILogger<SearchPageModel> _logger;
    private readonly EntryActionCoordinator _entryActions;
    private readonly ListOperationRunner _listOps;

    // Search debounce state: a new keystroke cancels the pending (or in-flight) search.
    private CancellationTokenSource? _searchDebounceCts;
    private string _activeSearchQuery = string.Empty;

    // Context the current results were fetched under, so a flip can invalidate them.
    private bool _hasSearchedThisSession;
    private bool _searchedWithAdultContent;
    private bool _searchedAuthenticated;

    public SearchPageModel(
        IAniListClient aniListClient,
        IAuthService authService,
        INavigationService navigationService,
        IUserFeedback feedback,
        ErrorReportService errorReportService,
        ILogger<SearchPageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _navigationService = navigationService;
        _feedback = feedback;
        _errorReportService = errorReportService;
        _logger = logger;
        _listOps = new ListOperationRunner(logger, feedback);

        // Long-press flows, as on Discover. A successful mutation is written back onto every
        // matching result so the status chips stay right without refetching the query.
        _entryActions = new EntryActionCoordinator(aniListClient, errorReportService, logger, new EntryActionHost
        {
            OpenDetailsAsync = entry => NavigateToMediaByIdAsync(entry.MediaId),
            OnEntrySavedInPlaceAsync = entry => { ApplyEntryToItems(entry); return Task.CompletedTask; },
            OnEntryStatusChangedAsync = entry => { ApplyEntryToItems(entry); return Task.CompletedTask; },
            OnEntryRemovedAsync = entry => { ClearEntryFromItems(entry.MediaId); return Task.CompletedTask; },
            SetErrorDetails = details => ErrorDetails = details,
        });

        // Results pagination: dedup + generation guarding live in the section; each new query
        // re-Seeds it (the generation bump supersedes any in-flight Load More).
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

    /// <summary>Search results with infinite scroll; re-seeded per query.</summary>
    public PaginatedSection<BrowseMediaItem> SearchSection { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>True once a query of <see cref="SearchMinLength"/>+ characters is entered — swaps the
    /// idle prompt for the results list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isSearchActive;

    /// <summary>Nothing typed yet (or below the minimum): show the "search for something" prompt.
    /// The Search tab has no rows to fall back to the way Discover did.</summary>
    public bool IsIdle => !IsSearchActive;

    /// <summary>True from first qualifying keystroke until the winning (non-superseded) fetch settles.</summary>
    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasNoSearchResults;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    public bool SearchIsLoadingMore => SearchSection.IsLoadingMore;

    /// <summary>
    /// Called from the page's OnAppearing. Discover used to fold this into its LoadAsync; with
    /// search on its own tab there is no load to piggyback on, so the check happens on appear.
    /// An auth flip changes the mediaListEntry chips riding the results, and an adult-toggle flip
    /// changes AdultFilter — stale results fetched under the old filter (18+ covers after turning
    /// the toggle off) must not linger until the user edits the query.
    /// </summary>
    public async Task OnAppearingAsync()
    {
        var displayAdult = AppSettings.DisplayAdultContent;
        var isAuthenticated = !string.IsNullOrWhiteSpace(await _authService.GetAccessTokenAsync());

        if (!_hasSearchedThisSession)
        {
            _searchedWithAdultContent = displayAdult;
            _searchedAuthenticated = isAuthenticated;
            return;
        }

        var authChanged = isAuthenticated != _searchedAuthenticated;
        var adultChanged = displayAdult != _searchedWithAdultContent;
        if (!authChanged && !adultChanged)
        {
            return;
        }

        _logger.LogInformation(
            "Search results invalidated (authChanged={AuthChanged}, adultChanged={AdultChanged}).",
            authChanged, adultChanged);

        _searchedWithAdultContent = displayAdult;
        _searchedAuthenticated = isAuthenticated;

        if (!string.IsNullOrEmpty(SearchText))
        {
            // Re-run the same query under the new context rather than dumping what the user typed.
            var query = SearchText;
            SearchText = string.Empty; // OnSearchTextChanged resets the section and cancels in-flight work
            SearchText = query;
        }
        else
        {
            SearchSection.Reset();
        }
    }

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
        _logger.LogInformation("Search firing for \"{Query}\"", query);

        // Record the context at issue time, not on success. If this is recorded only after the
        // fetch lands, a first search still in flight leaves _hasSearchedThisSession false, so a
        // user who tabs away, flips the adult toggle and returns hits OnAppearingAsync's
        // "nothing to invalidate" branch — which baselines the NEW context without cancelling
        // this request. The old-filter response would then land and be marked current, and no
        // later appearance would ever invalidate it. Recording here means that user takes the
        // normal path instead, which cancels this fetch and re-runs the query.
        _hasSearchedThisSession = true;
        _searchedWithAdultContent = AppSettings.DisplayAdultContent;

        try
        {
            var (items, pageInfo) = await _aniListClient.SearchAnimePageAsync(
                query, AdultFilter, page: 1, perPage: SearchPerPage, cancellationToken: token);
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
            _errorReportService.Record(ex, "Search");
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
        // Via ListOperationRunner: PaginatedSection.LoadMoreAsync only swallows cancellation, so a
        // network/API failure here would otherwise propagate out of the CollectionView's threshold
        // command and crash. The runner swallows + snackbars it.
        //
        // !IsSearching matters: between the debounce firing (which switches _activeSearchQuery to
        // the new text) and the page-1 response landing, the OLD query's items are still on screen
        // with HasNextPage set. Scrolling to the threshold in that window would fetch page 2 of the
        // NEW query and append it to the OLD results. A successful Seed bumps the section's
        // generation and discards that, but a FAILED page 1 never seeds — leaving the mixed list
        // visible. Gating on the in-flight search closes the window instead of relying on the seed.
        => SearchSection.CanLoadMore && !IsSearching
            ? _listOps.RunAsync(
                "Search · Load More",
                "search",
                0,
                () => SearchSection.LoadMoreAsync(),
                () => SearchSection.Items.Count)
            : Task.CompletedTask;

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
        _logger.LogInformation("NAVTRACE Search NavigateToMedia called with mediaId={MediaId}", mediaId);
        if (mediaId <= 0)
        {
            return;
        }

        // Media Details queries type: ANIME, so a non-anime id (search can return manga and
        // novels) would 404.
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
        foreach (var item in SearchSection.Items.Where(i => i.Node?.Id == entry.MediaId))
        {
            item.ApplyListEntry(entry);
        }
    }

    private void ClearEntryFromItems(int mediaId)
    {
        foreach (var item in SearchSection.Items.Where(i => i.Node?.Id == mediaId))
        {
            item.ClearListEntry();
        }
    }
}
