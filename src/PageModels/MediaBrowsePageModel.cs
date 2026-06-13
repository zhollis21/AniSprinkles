using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

/// <summary>
/// "View All" page for a Discover section (route <c>media-browse</c>): a vertical, infinite-scroll
/// browse list with the section's fixed sort. Everything section-specific (title, sort, filters,
/// rank numbers) derives from <see cref="DiscoverSectionDefinitions"/> — only the section enum name
/// travels through the route. Transient, like the other detail page models.
/// </summary>
public partial class MediaBrowsePageModel : ObservableObject
{
    private const int PageSize = 25;

    private readonly IAniListClient _aniListClient;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IUserFeedback _feedback;
    private readonly TimeProvider _timeProvider;
    private readonly IPreferences _preferences;
    private readonly ILogger<MediaBrowsePageModel> _logger;
    private readonly ListOperationRunner _listOps;
    private readonly EntryActionCoordinator _entryActions;
    private readonly PageLoadScope _scope = new();
    private readonly PaginatedSection<BrowseMediaItem> _items;

    private DiscoverSectionDefinition? _definition;

    public MediaBrowsePageModel(
        IAniListClient aniListClient,
        IAuthService authService,
        INavigationService navigationService,
        IUserFeedback feedback,
        ErrorReportService errorReportService,
        TimeProvider timeProvider,
        IPreferences preferences,
        ILogger<MediaBrowsePageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _navigationService = navigationService;
        _feedback = feedback;
        _timeProvider = timeProvider;
        _preferences = preferences;
        _logger = logger;
        _listOps = new ListOperationRunner(logger, feedback);

        // Shared with My Anime: the page opens in whatever look the user last picked anywhere.
        _currentViewMode = ListViewModePreference.Load(preferences);

        // Shared long-press flows; successful mutations are written back onto every row showing
        // that media so the chips stay consistent without a refetch.
        _entryActions = new EntryActionCoordinator(aniListClient, errorReportService, logger, new EntryActionHost
        {
            OpenDetailsAsync = entry => NavigateToMediaByIdAsync(entry.MediaId),
            OnEntrySavedInPlaceAsync = entry => { ApplyEntryToItems(entry); return Task.CompletedTask; },
            OnEntryStatusChangedAsync = entry => { ApplyEntryToItems(entry); return Task.CompletedTask; },
            OnEntryRemovedAsync = entry => { ClearEntryFromItems(entry.MediaId); return Task.CompletedTask; },
            SetErrorDetails = details => ErrorDetails = details,
        });

        // initialSort is a placeholder until LoadAsync re-seeds with the section's real sort —
        // the fetch delegate ignores the passed sort in favor of the definition's.
        _items = new PaginatedSection<BrowseMediaItem>(
            "POPULARITY_DESC",
            FetchPageAsync,
            item => item.Node?.Id ?? 0,
            StampItems);
        _items.Changed += OnItemsChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.InitialLoading;

    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSection))]
    private string _pageTitle = "Browse";

    public bool HasSection => _definition is not null;

    public ObservableCollection<BrowseMediaItem> Items => _items.Items;

    public bool HasItems => _items.Items.Count > 0;

    public bool IsLoadingMore => _items.IsLoadingMore;

    public bool ShowEmptyState => !HasItems && !_items.IsBusy;

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorSubtitle = string.Empty;

    [ObservableProperty]
    private string _errorIconGlyph = string.Empty;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    [ObservableProperty]
    private bool _canRetry = true;

    // ── View mode (mirrors MyAnimePageModel's switcher, persisted to the shared key) ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeIconGlyph))]
    private ListViewMode _currentViewMode = ListViewMode.Large;

    public string ViewModeIconGlyph => CurrentViewMode switch
    {
        ListViewMode.Large => FluentIconsRegular.Grid24,
        ListViewMode.Compact => FluentIconsRegular.TextBulletListSquare24,
        _ => FluentIconsRegular.List24,
    };

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

    partial void OnCurrentViewModeChanged(ListViewMode value)
        => ListViewModePreference.Save(_preferences, value);

    partial void OnCurrentStateChanged(PageState oldValue, PageState newValue)
        => _logger.LogInformation("PageState: {OldState} → {NewState} (key={StateKey})", oldValue, newValue, CurrentStateKey ?? "(null)");

    public async Task LoadAsync(DiscoverSection? section)
    {
        if (section is null)
        {
            ShowError("Not Found", "Unknown browse section.", canRetry: false);
            return;
        }

        if (_definition?.Section == section && HasItems)
        {
            CurrentState = PageState.Content;
            return;
        }

        _definition = DiscoverSectionDefinitions.Get(section.Value);
        PageTitle = _definition.Title;
        var token = _scope.Begin();

        IsBusy = true;
        CurrentState = PageState.InitialLoading;
        _items.Reset();

        _logger.LogInformation("NAVTRACE MediaBrowse load start (section {Section})", section);

        try
        {
            var (items, pageInfo) = await FetchPageAsync(1, _definition.Sort, token).ConfigureAwait(true);
            _items.Seed(items, pageInfo);
            CurrentState = PageState.Content;
            _logger.LogInformation(
                "NAVTRACE MediaBrowse seeded {Count} items (section {Section}, hasNext={HasNext})",
                _items.Items.Count, section, pageInfo?.HasNextPage);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("NAVTRACE MediaBrowse load cancelled (section {Section})", section);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NAVTRACE MediaBrowse load failed (section {Section})", section);
            var apiEx = ex as AniListApiException;
            ShowError(
                apiEx?.UserTitle ?? "Something Went Wrong",
                apiEx?.UserSubtitle ?? "Failed to load this list. Try again or check back later.",
                canRetry: true,
                details: ex.Message,
                iconGlyph: apiEx?.IconGlyph);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CancelInFlight() => _scope.Cancel();

    private Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> FetchPageAsync(
        int page, string sort, CancellationToken cancellationToken)
    {
        var definition = _definition
            ?? throw new InvalidOperationException("MediaBrowse fetch before a section was applied.");

        // Season math, adult-toggle resolution, and the format pin live in the shared helper —
        // the Discover rows page through the exact same code path.
        return DiscoverSectionFetch.PageAsync(
            _aniListClient, _timeProvider, definition, page, PageSize, cancellationToken);
    }

    private void StampItems(IReadOnlyList<BrowseMediaItem> added, string sort)
    {
        // Called before the items enter the ObservableCollection, so Items.Count is the pre-append
        // count and ranks continue seamlessly across pages (dedup keeps them contiguous).
        // Badge off the section's definition sort, not the passed `sort`: this section is seeded
        // with a placeholder initial sort (there's no sort picker here), so PaginatedSection.Sort
        // stays that placeholder and would badge every section as popularity.
        var badgeSort = _definition?.Sort ?? sort;
        var baseRank = _items.Items.Count;
        for (var i = 0; i < added.Count; i++)
        {
            if (_definition?.ShowsRank == true)
            {
                added[i].Rank = baseRank + i + 1;
            }

            added[i].MetricBadge = MediaMetricBadges.ForMediaSort(added[i].Node, badgeSort);
        }
    }

    private void OnItemsChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsLoadingMore));
        OnPropertyChanged(nameof(ShowEmptyState));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMore()
        => _listOps.RunAsync(
            $"MediaBrowse {_definition?.Section} · Load More",
            "browse",
            (int?)_definition?.Section ?? 0,
            () => _items.LoadMoreAsync(_scope.EnsureActive()),
            () => _items.Items.Count);

    private bool CanLoadMore() => _items.CanLoadMore;

    [RelayCommand]
    private Task RetryLoad()
    {
        var section = _definition?.Section;
        _definition = null; // force a full reload (LoadAsync short-circuits on same section + items)
        return LoadAsync(section);
    }

    [RelayCommand]
    private async Task NavigateToMedia(BrowseMediaItem? item)
    {
        // A long-press release still triggers the row's tap recognizer — swallow it so the
        // action sheet doesn't get a navigation underneath.
        if (Views.CollectionViewLongPress.ShouldSuppressTap())
        {
            return;
        }

        var media = item?.Node;
        var mediaId = media?.Id ?? 0;
        _logger.LogInformation("NAVTRACE MediaBrowse→Media with id={MediaId}", mediaId);
        if (mediaId <= 0)
        {
            return;
        }

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
        foreach (var item in Items.Where(i => i.Node?.Id == entry.MediaId))
        {
            item.ApplyListEntry(entry);
        }
    }

    private void ClearEntryFromItems(int mediaId)
    {
        foreach (var item in Items.Where(i => i.Node?.Id == mediaId))
        {
            item.ClearListEntry();
        }
    }

    private void ShowError(string title, string subtitle, bool canRetry, string details = "", string? iconGlyph = null)
    {
        ErrorTitle = title;
        ErrorSubtitle = subtitle;
        ErrorIconGlyph = iconGlyph ?? FluentIconsRegular.ErrorCircle24;
        ErrorDetails = details;
        CanRetry = canRetry;
        CurrentState = PageState.Error;
    }
}
