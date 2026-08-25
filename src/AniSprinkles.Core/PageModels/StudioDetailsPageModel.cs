using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

public partial class StudioDetailsPageModel : DetailsPageModelBase<Studio>
{
    private const int PageSize = 25;
    private const string ProductionsDefaultSort = "POPULARITY_DESC";

    private readonly PaginatedSection<StudioMediaEdge> _productions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStudio))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(HasSiteUrl))]
    [NotifyPropertyChangedFor(nameof(IsFavourite))]
    [NotifyPropertyChangedFor(nameof(FavouritesDisplay))]
    [NotifyPropertyChangedFor(nameof(HasFavourites))]
    private Studio? _studio;

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
        IAuthService authService,
        INavigationService navigationService,
        IUserFeedback feedback,
        IExternalBrowser browser,
        ErrorReportService errorReportService,
        ILogger<StudioDetailsPageModel> logger)
        : base(aniListClient, authService, navigationService, feedback, browser, errorReportService, logger)
    {
        _productions = new PaginatedSection<StudioMediaEdge>(
            ProductionsDefaultSort,
            FetchProductionsPageAsync,
            edge => edge.Node?.Id ?? 0,
            StampProductionBadges,
            DetailsListSorters.SortStudioProductions);
        _productions.Changed += OnProductionsChanged;
    }

    public ObservableCollection<StudioMediaEdge> DisplayedProductions => _productions.Items;

    /// <inheritdoc />
    protected override IEnumerable<IDisplayProjection> DisplayProjections => DisplayedProductions;

    public bool HasProductions => _productions.Items.Count > 0;

    public bool ProductionsBusy => _productions.IsBusy;

    public string ProductionsSort => _productions.Sort;

    // No productions loaded and nothing in flight → show the friendly empty state instead of a blank section.
    public bool ShowProductionsEmptyState => !HasProductions && !ProductionsBusy;

    public bool ShowProductionsSection => HasProductions || ShowProductionsEmptyState;

    public string ProductionsEmptyMessage => "No productions found for this studio.";

    public bool HasStudio => Studio is not null;

    public string PageTitle => Studio?.DisplayName ?? "Studio";

    // ---- Spine ------------------------------------------------------------------------------------

    protected override Studio? Entity
    {
        get => Studio;
        set => Studio = value;
    }

    protected override string EntityNoun => "studio";

    protected override string TracePrefix => "StudioDetails";

    protected override FavouriteKind FavouriteKind => FavouriteKind.Studio;

    protected override string? SiteUrl => Studio?.SiteUrl;

    public Task LoadAsync(int studioId) => LoadCoreAsync(studioId);

    protected override Task<Studio?> FetchAsync(int id, CancellationToken cancellationToken)
        => AniList.GetStudioAsync(id, mediaPerPage: PageSize, cancellationToken: cancellationToken);

    protected override void SeedSections(Studio entity)
        => _productions.Seed(entity.Media, entity.MediaPageInfo);

    protected override void ResetForNewEntity()
    {
        _productions.Reset();
        ResetProductionsSortSelection();
    }

    protected override string DescribeSeededSections() => $"{_productions.Items.Count} productions";

    // The favourites display binds through Studio.*, so re-raise Studio to refresh those nested bindings.
    protected override void OnFavouriteChanged() => OnPropertyChanged(nameof(Studio));

    partial void OnStudioChanged(Studio? value) => ToggleFavouriteCommand.NotifyCanExecuteChanged();

    // ---- Productions ------------------------------------------------------------------------------

    private Task<(IReadOnlyList<StudioMediaEdge> Items, PageInfo? PageInfo)> FetchProductionsPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => AniList.LoadStudioMediaPageAsync(LoadedId, page, sort, PageSize, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanLoadMoreProductions))]
    private Task LoadMoreProductions()
        => ListOps.RunAsync(
            "Studio Productions · Load More",
            "studio",
            LoadedId,
            () => _productions.LoadMoreAsync(Scope.EnsureActive()),
            () => _productions.Items.Count);

    private bool CanLoadMoreProductions() => _productions.CanLoadMore;

    [RelayCommand]
    private Task SelectProductionsSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, _productions.Sort, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return ListOps.RunAsync(
            $"Studio Productions · sort→{code}",
            "studio",
            LoadedId,
            () => _productions.ChangeSortAsync(code, Scope.EnsureActive()),
            () => _productions.Items.Count,
            onComplete: () => SyncSortSelection(ProductionsSortOptions, _productions.Sort));
    }

    private void OnProductionsChanged()
    {
        OnPropertyChanged(nameof(HasProductions));
        OnPropertyChanged(nameof(ProductionsBusy));
        OnPropertyChanged(nameof(ProductionsSort));
        OnPropertyChanged(nameof(ShowProductionsEmptyState));
        OnPropertyChanged(nameof(ShowProductionsSection));
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
}
