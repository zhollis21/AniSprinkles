using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.PageModels;

public partial class StudioDetailsPageModel : ObservableObject
{
    private readonly IAniListClient _aniListClient;
    private readonly INavigationService _navigationService;
    private readonly ILogger<StudioDetailsPageModel> _logger;

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
        ILogger<StudioDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _navigationService = navigationService;
        _logger = logger;

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

    public string FavouritesDisplay => FormatFavourites(Studio?.Favourites);

    public bool HasSiteUrl => !string.IsNullOrWhiteSpace(Studio?.SiteUrl);

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
        if (Studio is null || Studio.Id != studioId)
        {
            CurrentState = PageState.InitialLoading;
        }

        _productions.Reset();
        ResetProductionsSortSelection();

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("NAVTRACE StudioDetails load start (studio {StudioId})", studioId);

        try
        {
            var studio = await _aniListClient.GetStudioAsync(studioId, cancellationToken: token).ConfigureAwait(true);
            if (studio is null)
            {
                _logger.LogInformation("NAVTRACE StudioDetails not found in {ElapsedMs}ms (studio {StudioId})", stopwatch.ElapsedMilliseconds, studioId);
                ShowError("Not Found", "We couldn't find this studio.", canRetry: false);
                return;
            }

            Studio = studio;
            _productions.Seed(studio.Media.ToList(), studio.MediaPageInfo);

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
        => RunTracedListOpAsync(
            "Studio Productions · Load More",
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

        return RunTracedListOpAsync(
            $"Studio Productions · sort→{code}",
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
            await ShowToastAsync("Manga & Novel details aren't supported yet.");
            return;
        }

        await _navigationService.GoToAsync("media-details", animate: false, new Dictionary<string, object>
        {
            ["mediaId"] = mediaId,
        });
    }

    private async Task RunTracedListOpAsync(string op, Func<Task> operation, Func<int> loadedCount, Action? onComplete = null)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("LISTTRACE {Op} start (studio {StudioId})", op, _loadedStudioId);

        Exception? failure = null;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        stopwatch.Stop();
        onComplete?.Invoke();

        if (failure is null)
        {
            _logger.LogInformation(
                "LISTTRACE {Op} completed in {ElapsedMs}ms ({Count} loaded); UI render follows",
                op, stopwatch.ElapsedMilliseconds, loadedCount());
            return;
        }

        _logger.LogWarning(failure, "LISTTRACE {Op} failed in {ElapsedMs}ms (studio {StudioId})", op, stopwatch.ElapsedMilliseconds, _loadedStudioId);
        await ShowListErrorSnackbarAsync(failure).ConfigureAwait(true);
    }

    private Task ShowListErrorSnackbarAsync(Exception ex)
    {
        var message = ex is AniListApiException apiEx
            ? apiEx.UserSubtitle
            : "Couldn't update the list. Check your connection and try again.";
        return ShowSnackbarAsync(message);
    }

    private async Task ShowSnackbarAsync(string message)
    {
        try
        {
            await Snackbar.Make(message, duration: TimeSpan.FromSeconds(4)).Show().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snackbar display failed");
        }
    }

    private async Task ShowToastAsync(string message)
    {
        try
        {
            await Toast.Make(message, ToastDuration.Short).Show().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toast display failed");
        }
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
            edge.MetricBadge = BuildProductionMetricBadge(edge.Node, sort);
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

    private static ItemMetricBadge? BuildProductionMetricBadge(RelatedMedia? media, string sort)
    {
        if (media is null)
        {
            return null;
        }

        return sort switch
        {
            "POPULARITY_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.People24,
                IconColor = Color.FromArgb("#FF9500"),
                Text = media.PopularityOrZero,
            },
            "SCORE_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Star24,
                IconColor = Color.FromArgb("#FFCC00"),
                Text = media.ScoreOrDash,
            },
            "FAVOURITES_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Heart24,
                IconColor = Color.FromArgb("#FF2D95"),
                Text = media.FavouritesOrZero,
            },
            "START_DATE_DESC" or "START_DATE" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Calendar24,
                IconColor = Color.FromArgb("#00C2FF"),
                Text = media.YearOrDash,
            },
            _ => null,
        };
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

    private static string FormatFavourites(int? favourites)
    {
        if (favourites is null or <= 0)
        {
            return string.Empty;
        }

        if (favourites >= 1000)
        {
            return (favourites.Value / 1000.0).ToString("0.#k", CultureInfo.InvariantCulture);
        }

        return favourites.Value.ToString(CultureInfo.InvariantCulture);
    }
}
