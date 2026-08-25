using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

/// <summary>
/// Discover page (issue #15): public browse sections, no auth required. Search used to live
/// here behind a toolbar toggle; it moved to its own tab in #43 — see <see cref="SearchPageModel"/>.
/// Singleton, like the other tab page models — the section data doubles as the TTL cache:
/// Discover data changes slowly, so revisits within <see cref="SectionsTtl"/> skip the network
/// entirely and pull-to-refresh is the explicit bypass. A flipped adult-content toggle also
/// invalidates the cache, since it changes both the query filters and which sections exist.
/// </summary>
public partial class DiscoverPageModel : ObservableObject
{
    private static readonly TimeSpan SectionsTtl = TimeSpan.FromMinutes(20);
    private const int SectionPerPage = 20;

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

    /// <summary>
    /// The DisplayAdultContent value the rows' current items were loaded under. Decides whether a
    /// revisit has to refetch, and — since it describes exactly the policy the rows are holding —
    /// is also what row paging is pinned to (#118). Assigned only on a successful load, so a failed
    /// refresh deliberately leaves it describing the rows the user is still looking at.
    /// </summary>
    private bool _loadedWithAdultContent;
    private bool _loadedAuthenticated;

    /// <summary>Re-renders the row cards when the title language moves under them (#127).</summary>
    private readonly TitleProjectionWatcher _titleProjections = new();

    /// <summary>True from the moment <see cref="LoadAsync"/> is entered until it finishes, which
    /// includes the auth read that runs before <c>IsBusy</c> is set. Load More checks this so no
    /// page can be fetched under a context the in-progress refresh is about to replace.</summary>
    private bool _refreshEvaluating;

    public DiscoverPageModel(
        IAniListClient aniListClient,
        IAuthService authService,
        INavigationService navigationService,
        IUserFeedback feedback,
        IDialogService dialogs,
        ListEntryStatusFlow statusFlow,
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
                // The seeded policy, not a live AppSettings read: page 2 must match the policy page
                // 1 was seeded under, or a commit landing mid-session mixes 18+ and SFW items into
                // one row (#118). Read from the field at call time, so a re-seed moves every row's
                // paging onto the new policy together.
                (page, _, ct) => DiscoverSectionFetch.PageAsync(
                    _aniListClient, _timeProvider, definition, _loadedWithAdultContent, page, SectionPerPage, ct)))
            .ToList();

        // Shared long-press flows. A successful mutation is written back onto EVERY item showing
        // that media (the same anime can sit in several rows), keeping the chips and the
        // TTL-cached sections consistent without a refetch.
        _entryActions = new EntryActionCoordinator(aniListClient, errorReportService, dialogs, feedback, statusFlow, logger, new EntryActionHost
        {
            OpenDetailsAsync = entry => NavigateToMediaByIdAsync(entry.MediaId),
            OnEntrySavedInPlaceAsync = entry => { ApplyEntryToItems(entry); return Task.CompletedTask; },
            OnEntryStatusChangedAsync = entry => { ApplyEntryToItems(entry); return Task.CompletedTask; },
            OnEntryRemovedAsync = entry => { ClearEntryFromItems(entry.MediaId); return Task.CompletedTask; },
            SetErrorDetails = details => ErrorDetails = details,
        });
    }

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

        // A refresh being in flight means the row is not marked busy — PaginatedSection only knows
        // about its own operations. That matters because DiscoverSectionFetch reads
        // AppSettings.DisplayAdultContent live, per page: a Load More racing an adult-toggle
        // refresh fetches under the NEW filter and appends onto items fetched under the OLD one,
        // mixing 18+ results into a row that was SFW. A successful refresh re-Seeds and the
        // generation bump discards the stray page, but a FAILED refresh never seeds — and Discover
        // deliberately keeps showing content on refresh failure, so the mixed row is what the user
        // is left looking at.
        //
        // Both flags are needed: IsBusy only goes up once the fetch starts, leaving the auth read
        // ahead of it as an open window.
        if (IsBusy || _refreshEvaluating)
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
        if (IsBusy || _refreshEvaluating)
        {
            return;
        }

        // _refreshEvaluating is raised before the FIRST await, unlike IsBusy which is only set
        // once the fetch actually starts. The auth read in LoadCoreAsync suspends while IsBusy is
        // still false, so a row near its threshold could start a Load More that sailed past
        // LoadMoreSection's IsBusy check, fetch under the new filter, and append onto rows still
        // holding the old context. Gating Load More on IsBusy alone left exactly that window open.
        _refreshEvaluating = true;
        try
        {
            await LoadCoreAsync(forceReload);
        }
        finally
        {
            _refreshEvaluating = false;
        }
    }

    private async Task LoadCoreAsync(bool forceReload)
    {
        // Ahead of the cache short-circuit below: a title-language change has to reach the rows on
        // screen even when the TTL means no fetch runs (#127).
        _titleProjections.RefreshIfTitleLanguageChanged(Rows.SelectMany(r => r.Items));

        var displayAdult = AppSettings.DisplayAdultContent;
        var now = _timeProvider.GetUtcNow();

        // Two things invalidate the cached, viewer-relative section data:
        //  - an auth flip: mediaListEntry chips ride the results, so they must be refetched.
        //  - an adult-toggle flip: it changes both the query filters and which sections exist.
        // SearchPageModel runs the same check against its own results in OnAppearingAsync.
        var isAuthenticated = !string.IsNullOrWhiteSpace(await _authService.GetAccessTokenAsync());
        var authChanged = _hasLoaded && isAuthenticated != _loadedAuthenticated;
        var adultChanged = _hasLoaded && _loadedWithAdultContent != displayAdult;
        if (authChanged || adultChanged)
        {
            _logger.LogInformation(
                "Discover cache invalidated (authChanged={AuthChanged}, adultChanged={AdultChanged}).",
                authChanged, adultChanged);
            forceReload = true;
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
                perPage: SectionPerPage);

            // Re-seeding resets each row to page 1 and supersedes any in-flight row Load More.
            foreach (var row in Rows)
            {
                row.Seed(SectionPageFor(sections, row.Definition.Section));
            }

            _hasLoaded = true;
            _lastSuccessfulLoadUtc = _timeProvider.GetUtcNow();
            // Also re-pins every row's paging onto the policy this seed used. Safe here rather than
            // beside the Seed() calls above because nothing awaits in between, so no Load More can
            // interleave.
            _loadedWithAdultContent = displayAdult;
            _loadedAuthenticated = isAuthenticated;

            // The rows were just rebuilt from the current settings; nothing for the next appearance
            // to re-project.
            _titleProjections.MarkRendered();
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
                ErrorIconGlyph = apiEx?.IconGlyph ?? Glyphs.Regular.ErrorCircle24;
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
        if (LongPressTapSuppressor.ShouldSuppressTap())
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
        // non-anime id would 404.
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
        Rows.SelectMany(row => row.Items);

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
