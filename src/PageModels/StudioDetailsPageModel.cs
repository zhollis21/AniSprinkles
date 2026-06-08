using System.Collections.ObjectModel;
using System.Diagnostics;
using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

public partial class StudioDetailsPageModel : ObservableObject
{
    private readonly IAniListClient _aniListClient;
    private readonly INavigationService _navigationService;
    private readonly IUserFeedback _feedback;
    private readonly ILogger<StudioDetailsPageModel> _logger;
    private readonly ListOperationRunner _listOps;

    private const int PageSize = 25;
    private const string ProductionsDefaultSort = "POPULARITY_DESC";

    private int _loadedStudioId;
    private readonly PageLoadScope _scope = new();
    private readonly PaginatedSection<StudioMediaEdge> _productions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.InitialLoading;

    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStudio))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(HasFavourites))]
    [NotifyPropertyChangedFor(nameof(FavouritesDisplay))]
    [NotifyPropertyChangedFor(nameof(HasSiteUrl))]
    [NotifyPropertyChangedFor(nameof(HasProductionsTotal))]
    [NotifyPropertyChangedFor(nameof(ProductionsTotalDisplay))]
    private Studio? _studio;

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

    public IReadOnlyList<SortOption> ProductionsSortOptions { get; } =
    [
        new SortOption { Code = "POPULARITY_DESC", Display = "Most Watched", IsSelected = true },
        new SortOption { Code = "SCORE_DESC",      Display = "Avg Score" },
        new SortOption { Code = "FAVOURITES_DESC", Display = "Most Favorited" },
        new SortOption { Code = "START_DATE_DESC", Display = "Newest" },
        new SortOption { Code = "START_DATE",      Display = "Oldest" },
        new SortOption { Code = "TITLE_ROMAJI",    Display = "Title" },
    ];

    public StudioDetailsPageModel(
        IAniListClient aniListClient,
        INavigationService navigationService,
        IUserFeedback feedback,
        ILogger<StudioDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _navigationService = navigationService;
        _feedback = feedback;
        _logger = logger;
        _listOps = new ListOperationRunner(logger, feedback);

        _productions = new PaginatedSection<StudioMediaEdge>(
            ProductionsDefaultSort,
            FetchProductionsPageAsync,
            edge => edge.Node?.Id ?? 0,
            StampProductionBadges,
            DetailsListSorters.SortStudioProductions);
        _productions.Changed += OnProductionsChanged;
    }

    public ObservableCollection<StudioMediaEdge> DisplayedProductions => _productions.Items;

    public bool HasProductions => _productions.Items.Count > 0;

    public bool ProductionsBusy => _productions.IsBusy;

    public string ProductionsSort => _productions.Sort;

    public bool HasStudio => Studio is not null;

    public string PageTitle => Studio?.DisplayName ?? "Studio";

    public bool HasFavourites => Studio?.Favourites is > 0;

    public string FavouritesDisplay => MetricFormat.Compact(Studio?.Favourites);

    public bool HasSiteUrl => !string.IsNullOrWhiteSpace(Studio?.SiteUrl);

    // Total productions across all pages (AniList pageInfo.total), independent of how many are paged in.
    public bool HasProductionsTotal => Studio?.MediaPageInfo?.Total is > 0;

    public string ProductionsTotalDisplay => MetricFormat.Compact(Studio?.MediaPageInfo?.Total);

    partial void OnCurrentStateChanged(PageState oldValue, PageState newValue)
        => _logger.LogInformation("PageState: {OldState} → {NewState} (key={StateKey})", oldValue, newValue, CurrentStateKey ?? "(null)");

    public async Task LoadAsync(int studioId)
    {
        if (studioId <= 0)
        {
            ShowError("Not Found", "Invalid studio id.", canRetry: false);
            return;
        }

        if (Studio is not null && Studio.Id == studioId)
        {
            CurrentState = PageState.Content;
            return;
        }

        _loadedStudioId = studioId;
        var token = _scope.Begin();

        IsBusy = true;
        CurrentState = PageState.InitialLoading;

        _productions.Reset();
        ResetProductionsSortSelection();

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("NAVTRACE StudioDetails load start (studio {StudioId})", studioId);

        try
        {
            var studio = await _aniListClient.GetStudioAsync(studioId, mediaPerPage: PageSize, cancellationToken: token).ConfigureAwait(true);
            if (studio is null)
            {
                _logger.LogInformation("NAVTRACE StudioDetails not found in {ElapsedMs}ms (studio {StudioId})", stopwatch.ElapsedMilliseconds, studioId);
                ShowError("Not Found", "We couldn't find this studio.", canRetry: false);
                return;
            }

            Studio = studio;
            _productions.Seed(studio.Media, studio.MediaPageInfo);

            CurrentState = PageState.Content;
            _logger.LogInformation(
                "NAVTRACE StudioDetails fetch+seed in {ElapsedMs}ms (studio {StudioId}, {Productions} productions); UI render follows",
                stopwatch.ElapsedMilliseconds, studioId, _productions.Items.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("NAVTRACE StudioDetails load cancelled after {ElapsedMs}ms (studio {StudioId})", stopwatch.ElapsedMilliseconds, studioId);
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;
            var isNotFound = apiEx?.Kind == ApiErrorKind.NotFound;
            if (isNotFound)
            {
                _logger.LogWarning(ex, "NAVTRACE StudioDetails not found on AniList in {ElapsedMs}ms (studio {StudioId})", stopwatch.ElapsedMilliseconds, studioId);
            }
            else
            {
                _logger.LogError(ex, "NAVTRACE StudioDetails load failed in {ElapsedMs}ms (studio {StudioId})", stopwatch.ElapsedMilliseconds, studioId);
            }

            var (title, subtitle) = DescribeError(ex);
            ShowError(title, subtitle, canRetry: !isNotFound, details: isNotFound ? string.Empty : ex.Message, iconGlyph: apiEx?.IconGlyph);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CancelInFlight() => _scope.Cancel();

    private Task<(IReadOnlyList<StudioMediaEdge> Items, PageInfo? PageInfo)> FetchProductionsPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => _aniListClient.LoadStudioMediaPageAsync(_loadedStudioId, page, sort, PageSize, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanLoadMoreProductions))]
    private Task LoadMoreProductions()
        => _listOps.RunAsync(
            "Studio Productions · Load More",
            "studio",
            _loadedStudioId,
            () => _productions.LoadMoreAsync(_scope.EnsureActive()),
            () => _productions.Items.Count);

    private bool CanLoadMoreProductions() => _productions.CanLoadMore;

    [RelayCommand]
    private Task SelectProductionsSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, _productions.Sort, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return _listOps.RunAsync(
            $"Studio Productions · sort→{code}",
            "studio",
            _loadedStudioId,
            () => _productions.ChangeSortAsync(code, _scope.EnsureActive()),
            () => _productions.Items.Count,
            onComplete: () => SyncSortSelection(ProductionsSortOptions, _productions.Sort));
    }

    [RelayCommand]
    private async Task OpenSiteUrl()
    {
        if (string.IsNullOrWhiteSpace(Studio?.SiteUrl))
        {
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(new Uri(Studio.SiteUrl), BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open AniList studio URL");
        }
    }

    [RelayCommand]
    private Task RetryLoad() => LoadAsync(_loadedStudioId);

    [RelayCommand]
    private async Task NavigateToMedia(RelatedMedia? media)
    {
        var mediaId = media?.Id ?? 0;
        _logger.LogInformation("NAVTRACE Studio→Media with id={MediaId}", mediaId);
        if (mediaId <= 0)
        {
            return;
        }

        if (media is { IsAnime: false })
        {
            _logger.LogInformation("NAVTRACE Studio→Media skipped non-anime {MediaId} (type={Type}).", mediaId, media.Type);
            await _feedback.ShowToastAsync("Manga & Novel details aren't supported yet.");
            return;
        }

        await _navigationService.GoToAsync("media-details", animate: false, new Dictionary<string, object>
        {
            ["mediaId"] = mediaId,
        });
    }

    private void OnProductionsChanged()
    {
        OnPropertyChanged(nameof(HasProductions));
        OnPropertyChanged(nameof(ProductionsBusy));
        OnPropertyChanged(nameof(ProductionsSort));
        LoadMoreProductionsCommand.NotifyCanExecuteChanged();
    }

    private void StampProductionBadges(IReadOnlyList<StudioMediaEdge> items, string sort)
    {
        foreach (var edge in items)
        {
            edge.MetricBadge = MediaMetricBadges.ForMediaSort(edge.Node, sort);
        }
    }

    private void ResetProductionsSortSelection() => SyncSortSelection(ProductionsSortOptions, ProductionsDefaultSort);

    private static void SyncSortSelection(IReadOnlyList<SortOption> options, string code)
    {
        foreach (var opt in options)
        {
            opt.IsSelected = string.Equals(opt.Code, code, StringComparison.Ordinal);
        }
    }

    private static (string Title, string Subtitle) DescribeError(Exception ex)
        => ex is AniListApiException apiEx
            ? (apiEx.UserTitle, apiEx.UserSubtitle)
            : ("Something Went Wrong", "Failed to load studio details.");

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
