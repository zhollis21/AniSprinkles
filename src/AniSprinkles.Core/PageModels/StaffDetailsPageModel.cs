using System.Collections.ObjectModel;
using System.Globalization;
using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

public partial class StaffDetailsPageModel : DetailsPageModelBase<Staff>
{
    private const int PageSize = 25;
    private const string VoiceRolesDefaultSort = "FAVOURITES_DESC";
    private const string ProductionRolesDefaultSort = "POPULARITY_DESC";

    private ParsedDescription _parsedDescription = ParsedDescription.Empty;

    // Voice Roles (Staff.characters) and Production Roles (Staff.staffMedia) are two genuinely
    // separate AniList connections — each lazily paged and server-side sorted, fully independent.
    private readonly PaginatedSection<StaffCharacterEdge> _voiceRoles;
    private readonly PaginatedSection<StaffMediaEdge> _productionRoles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStaff))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(IsFavourite))]
    [NotifyPropertyChangedFor(nameof(FavouritesDisplay))]
    [NotifyPropertyChangedFor(nameof(HasFavourites))]
    [NotifyPropertyChangedFor(nameof(IsBirthdayToday))]
    [NotifyPropertyChangedFor(nameof(BioStats))]
    [NotifyPropertyChangedFor(nameof(HasBioStats))]
    [NotifyPropertyChangedFor(nameof(BioProse))]
    [NotifyPropertyChangedFor(nameof(HasBioProse))]
    [NotifyPropertyChangedFor(nameof(IsDescriptionTruncated))]
    [NotifyPropertyChangedFor(nameof(HasSpoilers))]
    [NotifyPropertyChangedFor(nameof(BornStatDisplay))]
    [NotifyPropertyChangedFor(nameof(AgeStatDisplay))]
    [NotifyPropertyChangedFor(nameof(QuickFactChips))]
    [NotifyPropertyChangedFor(nameof(HasQuickFactChips))]
    [NotifyPropertyChangedFor(nameof(HasSiteUrl))]
    private Staff? _staff;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BioProse))]
    [NotifyPropertyChangedFor(nameof(BioStats))]
    private bool _isShowingSpoilers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionMaxLines))]
    private bool _isDescriptionExpanded;

    public IReadOnlyList<SortOption> VoiceRolesSortOptions { get; } =
    [
        new SortOption { Code = "FAVOURITES_DESC", Display = "Most Favorited", IsSelected = true },
        new SortOption { Code = "ROLE",            Display = "Role" },
    ];

    public IReadOnlyList<SortOption> ProductionRolesSortOptions { get; } =
    [
        new SortOption { Code = "POPULARITY_DESC", Display = "Most Watched", IsSelected = true },
        new SortOption { Code = "SCORE_DESC",      Display = "Avg Score" },
        new SortOption { Code = "FAVOURITES_DESC", Display = "Most Favorited" },
        new SortOption { Code = "START_DATE_DESC", Display = "Newest" },
        new SortOption { Code = "START_DATE",      Display = "Oldest" },
        new SortOption { Code = "TITLE_ROMAJI",    Display = "Title" },
    ];

    public StaffDetailsPageModel(
        IAniListClient aniListClient,
        IAuthService authService,
        INavigationService navigationService,
        IUserFeedback feedback,
        IExternalBrowser browser,
        ErrorReportService errorReportService,
        ILogger<StaffDetailsPageModel> logger)
        : base(aniListClient, authService, navigationService, feedback, browser, errorReportService, logger)
    {
        _voiceRoles = new PaginatedSection<StaffCharacterEdge>(
            VoiceRolesDefaultSort,
            FetchVoiceRolesPageAsync,
            edge => edge.Node?.Id ?? 0,
            localSort: DetailsListSorters.SortVoiceRoles);
        _voiceRoles.Changed += OnVoiceRolesChanged;

        _productionRoles = new PaginatedSection<StaffMediaEdge>(
            ProductionRolesDefaultSort,
            FetchProductionRolesPageAsync,
            edge => (edge.Node?.Id ?? 0, edge.StaffRole ?? string.Empty),
            StampProductionBadges,
            DetailsListSorters.SortProductionRoles);
        _productionRoles.Changed += OnProductionRolesChanged;
    }

    // ---- Sections -------------------------------------------------------------------------------

    public ObservableCollection<StaffCharacterEdge> DisplayedVoiceRoles => _voiceRoles.Items;
    public ObservableCollection<StaffMediaEdge> DisplayedProductionRoles => _productionRoles.Items;

    /// <inheritdoc />
    /// <remarks>Both sections put a media title on the card, under the role.</remarks>
    protected override IEnumerable<IDisplayProjection> DisplayProjections =>
        DisplayedVoiceRoles.Cast<IDisplayProjection>().Concat(DisplayedProductionRoles);

    public bool HasVoiceRoles => _voiceRoles.Items.Count > 0;
    public bool HasProductionRoles => _productionRoles.Items.Count > 0;

    public bool VoiceRolesBusy => _voiceRoles.IsBusy;
    public bool ProductionRolesBusy => _productionRoles.IsBusy;

    public string VoiceRolesSort => _voiceRoles.Sort;
    public string ProductionRolesSort => _productionRoles.Sort;

    // ---- Hero / bio / quick facts (unchanged) ---------------------------------------------------

    public bool HasStaff => Staff is not null;

    public string PageTitle => Staff?.DisplayName ?? "Staff";

    public bool IsBirthdayToday => BirthdayChecker.IsBirthdayToday(Staff?.DateOfBirth, DateTime.Today);

    public IReadOnlyList<BioStatRow> BioStats =>
        _parsedDescription.Stats.Select(BuildBioStatRow).ToList();

    public bool HasBioStats => _parsedDescription.Stats.Count > 0;

    public string BioProse =>
        SpoilerHtmlProcessor.Process(
            AniListMarkdownProcessor.Process(_parsedDescription.Prose),
            IsShowingSpoilers);

    public bool HasBioProse => !string.IsNullOrWhiteSpace(_parsedDescription.Prose);

    public bool IsDescriptionTruncated => DescriptionTruncationHeuristic.IsTruncated(_parsedDescription.Prose);

    public int DescriptionMaxLines => IsDescriptionExpanded
        ? int.MaxValue
        : DescriptionTruncationHeuristic.CollapsedMaxLines;

    public bool HasSpoilers =>
        _parsedDescription.Stats.Any(s => s.IsRowSpoiler || s.IsValueSpoiler)
        || SpoilerHtmlProcessor.ContainsSpoilers(_parsedDescription.Prose);

    public string BornStatDisplay
        => FuzzyDateFormatter.Format(Staff?.DateOfBirth, includeYear: false) ?? "—";

    public string AgeStatDisplay
        => Staff?.Age is > 0 ? Staff.Age.Value.ToString(CultureInfo.InvariantCulture) : "—";

    public IReadOnlyList<QuickFactChip> QuickFactChips => BuildQuickFactChips();

    public bool HasQuickFactChips => QuickFactChips.Count > 0;

    partial void OnStaffChanged(Staff? value)
    {
        _parsedDescription = DescriptionParser.Parse(value?.Description);
        ToggleFavouriteCommand.NotifyCanExecuteChanged();
    }

    // ---- Spine ------------------------------------------------------------------------------------

    protected override Staff? Entity
    {
        get => Staff;
        set => Staff = value;
    }

    protected override string EntityNoun => "staff";

    protected override string TracePrefix => "StaffDetails";

    protected override FavouriteKind FavouriteKind => FavouriteKind.Staff;

    protected override string? SiteUrl => Staff?.SiteUrl;

    // "staff member" reads correctly here where the bare noun does not.
    protected override (string Title, string Subtitle) NotFoundError
        => ("Not Found", "We couldn't find this staff member.");

    public Task LoadAsync(int staffId) => LoadCoreAsync(staffId);

    protected override Task<Staff?> FetchAsync(int id, CancellationToken cancellationToken)
        => AniList.GetStaffAsync(id, cancellationToken: cancellationToken);

    protected override void SeedSections(Staff entity)
    {
        _voiceRoles.Seed(entity.Characters.ToList(), entity.CharactersPageInfo);
        _productionRoles.Seed(entity.StaffMedia.ToList(), entity.StaffMediaPageInfo);
    }

    protected override void ResetForNewEntity()
    {
        IsShowingSpoilers = false;
        IsDescriptionExpanded = false;
        _voiceRoles.Reset();
        _productionRoles.Reset();
        ResetVoiceRolesSortSelection();
        ResetProductionRolesSortSelection();
    }

    protected override string DescribeSeededSections()
        => $"{_voiceRoles.Items.Count} voice roles, {_productionRoles.Items.Count} production roles";

    // ---- Section fetches --------------------------------------------------------------------------

    private Task<(IReadOnlyList<StaffCharacterEdge> Items, PageInfo? PageInfo)> FetchVoiceRolesPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => AniList.LoadStaffCharactersPageAsync(LoadedId, page, sort, PageSize, cancellationToken);

    private Task<(IReadOnlyList<StaffMediaEdge> Items, PageInfo? PageInfo)> FetchProductionRolesPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => AniList.LoadStaffMediaPageAsync(LoadedId, page, sort, PageSize, cancellationToken);

    // ---- Commands -------------------------------------------------------------------------------

    // CanExecute gates the scroll-threshold trigger: with no next page or while a fetch/sort is in
    // flight, the CollectionView's RemainingItemsThresholdReached can't re-invoke this (which would
    // otherwise log a no-op LISTTRACE pair on every scroll-to-end). LoadMoreAsync stays guarded too.
    [RelayCommand(CanExecute = nameof(CanLoadMoreVoiceRoles))]
    private Task LoadMoreVoiceRoles()
        => ListOps.RunAsync(
            "Voice Roles · Load More",
            "staff",
            LoadedId,
            () => _voiceRoles.LoadMoreAsync(Scope.EnsureActive()),
            () => _voiceRoles.Items.Count);

    private bool CanLoadMoreVoiceRoles() => _voiceRoles.CanLoadMore;

    [RelayCommand(CanExecute = nameof(CanLoadMoreProductionRoles))]
    private Task LoadMoreProductionRoles()
        => ListOps.RunAsync(
            "Production Roles · Load More",
            "staff",
            LoadedId,
            () => _productionRoles.LoadMoreAsync(Scope.EnsureActive()),
            () => _productionRoles.Items.Count);

    private bool CanLoadMoreProductionRoles() => _productionRoles.CanLoadMore;

    [RelayCommand]
    private Task SelectVoiceRolesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, _voiceRoles.Sort, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return ListOps.RunAsync(
            $"Voice Roles · sort→{code}",
            "staff",
            LoadedId,
            () => _voiceRoles.ChangeSortAsync(code, Scope.EnsureActive()),
            () => _voiceRoles.Items.Count,
            onComplete: () => SyncSortSelection(VoiceRolesSortOptions, _voiceRoles.Sort));
    }

    [RelayCommand]
    private Task SelectProductionRolesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, _productionRoles.Sort, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return ListOps.RunAsync(
            $"Production Roles · sort→{code}",
            "staff",
            LoadedId,
            () => _productionRoles.ChangeSortAsync(code, Scope.EnsureActive()),
            () => _productionRoles.Items.Count,
            onComplete: () => SyncSortSelection(ProductionRolesSortOptions, _productionRoles.Sort));
    }

    [RelayCommand]
    private void ToggleSpoilers() => IsShowingSpoilers = !IsShowingSpoilers;

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    [RelayCommand]
    private async Task NavigateToCharacter(int characterId)
    {
        Logger.LogInformation("NAVTRACE Staff→Character with id={CharacterId}", characterId);
        if (characterId <= 0)
        {
            return;
        }

        await NavigationService.GoToAsync("character-details", animate: false, new Dictionary<string, object>
        {
            ["characterId"] = characterId,
        });
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private void OnVoiceRolesChanged()
    {
        OnPropertyChanged(nameof(HasVoiceRoles));
        OnPropertyChanged(nameof(VoiceRolesBusy));
        OnPropertyChanged(nameof(VoiceRolesSort));
        LoadMoreVoiceRolesCommand.NotifyCanExecuteChanged();
    }

    private void OnProductionRolesChanged()
    {
        OnPropertyChanged(nameof(HasProductionRoles));
        OnPropertyChanged(nameof(ProductionRolesBusy));
        OnPropertyChanged(nameof(ProductionRolesSort));
        LoadMoreProductionRolesCommand.NotifyCanExecuteChanged();
    }

    private void StampProductionBadges(IReadOnlyList<StaffMediaEdge> items, string sort)
    {
        foreach (var edge in items)
        {
            edge.MetricBadge = MediaMetricBadges.ForMediaSort(edge.Node, sort);
        }
    }

    private void ResetVoiceRolesSortSelection() => SyncSortSelection(VoiceRolesSortOptions, VoiceRolesDefaultSort);

    private void ResetProductionRolesSortSelection() => SyncSortSelection(ProductionRolesSortOptions, ProductionRolesDefaultSort);

    private static void SyncSortSelection(IReadOnlyList<SortOption> options, string code)
    {
        foreach (var opt in options)
        {
            opt.IsSelected = string.Equals(opt.Code, code, StringComparison.Ordinal);
        }
    }

    private BioStatRow BuildBioStatRow(DescriptionStatRow row)
    {
        var labelHidden = row.IsRowSpoiler && !IsShowingSpoilers;
        var valueHidden = (row.IsRowSpoiler || row.IsValueSpoiler) && !IsShowingSpoilers;

        return new BioStatRow
        {
            LabelDisplay = labelHidden ? Bar(row.Label.Length, max: 12) : row.Label,
            ValueDisplay = valueHidden ? Bar(row.Value.Length, max: 24) : row.Value,
            IsLabelSpoilerHidden = labelHidden,
            IsValueSpoilerHidden = valueHidden,
        };
    }

    private static string Bar(int sourceLength, int max)
        => new('█', Math.Clamp(sourceLength / 2, 4, max));

    private List<QuickFactChip> BuildQuickFactChips()
    {
        var chips = new List<QuickFactChip>();
        if (Staff is null)
        {
            return chips;
        }

        if (Staff.PrimaryOccupations is { Count: > 0 } occupations)
        {
            foreach (var occ in occupations.Where(o => !string.IsNullOrWhiteSpace(o)))
            {
                chips.Add(new QuickFactChip(occ));
            }
        }

        var yearsActive = YearsActiveFormatter.Format(Staff.YearsActive, Staff.DateOfDeath);
        if (yearsActive is not null)
        {
            chips.Add(new QuickFactChip(yearsActive));
        }

        if (!string.IsNullOrWhiteSpace(Staff.LanguageV2))
        {
            chips.Add(new QuickFactChip(Staff.LanguageV2));
        }

        if (!string.IsNullOrWhiteSpace(Staff.HomeTown))
        {
            chips.Add(new QuickFactChip(Staff.HomeTown));
        }

        return chips;
    }
}

public sealed record QuickFactChip(string Display);
