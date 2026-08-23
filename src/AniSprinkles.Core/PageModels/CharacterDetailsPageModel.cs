using System.Collections.ObjectModel;
using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

public partial class CharacterDetailsPageModel : DetailsPageModelBase<Character>
{
    private const int PageSize = 25;
    private const string AppearancesDefaultSort = "POPULARITY_DESC";

    // The fixed media ordering the Voice Actors list walks. Kept independent of the Appears In sort
    // so changing that sort never disturbs the voice-actor list (a UX bug in the prior design).
    private const string VoiceActorMediaSort = "POPULARITY_DESC";

    private ParsedDescription _parsedDescription = ParsedDescription.Empty;

    // The Appears In list and the deduped Voice Actors list are two fully independent views over
    // Character.media. Each owns its cursor; neither mutates the other.
    private readonly PaginatedSection<CharacterMediaEdge> _appearances;
    private readonly VoiceActorAggregator _voiceActors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacter))]
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
    [NotifyPropertyChangedFor(nameof(AlternativeNames))]
    [NotifyPropertyChangedFor(nameof(HasAlternativeNames))]
    [NotifyPropertyChangedFor(nameof(HasSiteUrl))]
    [NotifyPropertyChangedFor(nameof(AgeStatDisplay))]
    [NotifyPropertyChangedFor(nameof(BirthdayStatDisplay))]
    [NotifyPropertyChangedFor(nameof(GenderDisplay))]
    [NotifyPropertyChangedFor(nameof(HasGender))]
    [NotifyPropertyChangedFor(nameof(BloodTypeDisplay))]
    [NotifyPropertyChangedFor(nameof(HasBloodType))]
    [NotifyPropertyChangedFor(nameof(HasQuickFacts))]
    private Character? _character;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BioProse))]
    [NotifyPropertyChangedFor(nameof(BioStats))]
    [NotifyPropertyChangedFor(nameof(AlternativeNames))]
    private bool _isShowingSpoilers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionMaxLines))]
    private bool _isDescriptionExpanded;

    public IReadOnlyList<SortOption> AppearancesSortOptions { get; } =
    [
        new SortOption { Code = "POPULARITY_DESC", Display = "Most Watched", IsSelected = true },
        new SortOption { Code = "SCORE_DESC",      Display = "Avg Score" },
        new SortOption { Code = "FAVOURITES_DESC", Display = "Most Favorited" },
        new SortOption { Code = "START_DATE_DESC", Display = "Newest" },
        new SortOption { Code = "START_DATE",      Display = "Oldest" },
        new SortOption { Code = "TITLE_ROMAJI",    Display = "Title" },
    ];

    public CharacterDetailsPageModel(
        IAniListClient aniListClient,
        IAuthService authService,
        INavigationService navigationService,
        IUserFeedback feedback,
        IExternalBrowser browser,
        ErrorReportService errorReportService,
        ILogger<CharacterDetailsPageModel> logger)
        : base(aniListClient, authService, navigationService, feedback, browser, errorReportService, logger)
    {
        _appearances = new PaginatedSection<CharacterMediaEdge>(
            AppearancesDefaultSort,
            FetchAppearancesPageAsync,
            edge => edge.Node?.Id ?? 0,
            StampAppearanceBadges,
            DetailsListSorters.SortAppearances);
        _appearances.Changed += OnAppearancesChanged;

        _voiceActors = new VoiceActorAggregator(FetchVoiceActorMediaPageAsync);
        _voiceActors.Changed += OnVoiceActorsChanged;
    }

    // ---- Appears In -----------------------------------------------------------------------------

    // Bind XAML to these; the collection instances are stable for the page model's life.
    public ObservableCollection<CharacterMediaEdge> DisplayedAppearances => _appearances.Items;

    public bool HasAppearances => _appearances.Items.Count > 0;

    public bool AppearancesBusy => _appearances.IsBusy;

    public string AppearancesSort => _appearances.Sort;

    // ---- Voice Actors ---------------------------------------------------------------------------

    public ObservableCollection<VoiceActor> VoiceActors => _voiceActors.Items;

    public bool HasVoiceActors => !_voiceActors.IsEmpty;

    // Gate the section on its own state (not HasAppearances) so the empty-state message is
    // reachable even for a character with no media at all. After load this is always true —
    // there's always a definitive answer: a list, more to load, or the empty state.
    public bool ShowVoiceActorsSection => HasVoiceActors || VoiceActorsHasMore || ShowVoiceActorsEmptyState;

    public bool VoiceActorsHasMore => _voiceActors.HasMore;

    public bool IsCheckingVoiceActors => _voiceActors.IsChecking;

    // No voice actors found and nothing left to search → show the friendly empty state.
    public bool ShowVoiceActorsEmptyState => _voiceActors.IsEmpty && !_voiceActors.HasMore;

    // Found some and there's nothing more to search → quietly confirm the list is complete.
    public bool ShowVoiceActorsEndReached => !_voiceActors.IsEmpty && !_voiceActors.HasMore;

    public string VoiceActorsEmptyMessage => "No voice actors here — looks like this one lives in the manga panels. 📖";

    // ---- Hero / bio / stat surface (unchanged) --------------------------------------------------

    public bool HasCharacter => Character is not null;

    public string PageTitle => Character?.DisplayName ?? "Character";

    public bool IsBirthdayToday => BirthdayChecker.IsBirthdayToday(Character?.DateOfBirth, DateTime.Today);

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
        || SpoilerHtmlProcessor.ContainsSpoilers(_parsedDescription.Prose)
        || (Character?.Name?.AlternativeSpoiler is { Count: > 0 });

    public string AgeStatDisplay => string.IsNullOrWhiteSpace(Character?.Age) ? "—" : Character!.Age!;

    public string BirthdayStatDisplay
        => FuzzyDateFormatter.Format(Character?.DateOfBirth, includeYear: false) ?? "—";

    public string GenderDisplay => Character?.Gender ?? string.Empty;

    public bool HasGender => !string.IsNullOrWhiteSpace(Character?.Gender);

    public string BloodTypeDisplay => string.IsNullOrWhiteSpace(Character?.BloodType)
        ? string.Empty
        : $"Blood type {Character!.BloodType}";

    public bool HasBloodType => !string.IsNullOrWhiteSpace(Character?.BloodType);

    public bool HasQuickFacts => HasGender || HasBloodType;

    public IReadOnlyList<string> AlternativeNames => BuildAlternativeNames();

    public bool HasAlternativeNames => AlternativeNames.Count > 0;

    partial void OnCharacterChanged(Character? value)
    {
        _parsedDescription = DescriptionParser.Parse(value?.Description);
        ToggleFavouriteCommand.NotifyCanExecuteChanged();
    }

    // ---- Spine ------------------------------------------------------------------------------------

    protected override Character? Entity
    {
        get => Character;
        set => Character = value;
    }

    protected override string EntityNoun => "character";

    protected override string TracePrefix => "CharacterDetails";

    protected override FavouriteKind FavouriteKind => FavouriteKind.Character;

    protected override string? SiteUrl => Character?.SiteUrl;

    public Task LoadAsync(int characterId) => LoadCoreAsync(characterId);

    protected override Task<Character?> FetchAsync(int id, CancellationToken cancellationToken)
        => AniList.GetCharacterAsync(id, cancellationToken: cancellationToken);

    protected override void SeedSections(Character entity)
    {
        // Seed both independent sections from the single heavy first-page query.
        var pageOne = entity.Media.ToList();
        _appearances.Seed(pageOne, entity.MediaPageInfo);
        _voiceActors.Seed(pageOne, entity.MediaPageInfo);
    }

    protected override void ResetForNewEntity()
    {
        IsShowingSpoilers = false;
        IsDescriptionExpanded = false;
        _appearances.Reset();
        _voiceActors.Reset();
        ResetAppearancesSortSelection();
    }

    protected override string DescribeSeededSections()
        => $"{_appearances.Items.Count} appearances, {_voiceActors.Items.Count} VAs";

    // ---- Section fetches --------------------------------------------------------------------------

    private Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> FetchAppearancesPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => AniList.LoadCharacterMediaPageAsync(LoadedId, page, sort, PageSize, cancellationToken);

    private Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> FetchVoiceActorMediaPageAsync(
        int page, CancellationToken cancellationToken)
        => AniList.LoadCharacterMediaPageAsync(LoadedId, page, VoiceActorMediaSort, PageSize, cancellationToken);

    // ---- Commands -------------------------------------------------------------------------------

    // CanExecute gates the scroll-threshold trigger: with no next page or while a fetch/sort is in
    // flight, the CollectionView's RemainingItemsThresholdReached can't re-invoke this (which would
    // otherwise log a no-op LISTTRACE pair on every scroll-to-end). LoadMoreAsync stays guarded too.
    [RelayCommand(CanExecute = nameof(CanLoadMoreAppearances))]
    private Task LoadMoreAppearances()
        => ListOps.RunAsync(
            "Appears In · Load More",
            "character",
            LoadedId,
            () => _appearances.LoadMoreAsync(Scope.EnsureActive()),
            () => _appearances.Items.Count);

    private bool CanLoadMoreAppearances() => _appearances.CanLoadMore;

    [RelayCommand]
    private Task SelectAppearancesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, _appearances.Sort, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return ListOps.RunAsync(
            $"Appears In · sort→{code}",
            "character",
            LoadedId,
            () => _appearances.ChangeSortAsync(code, Scope.EnsureActive()),
            () => _appearances.Items.Count,
            // Keep the chip selection in sync with the sort that actually took effect.
            onComplete: () => SyncAppearancesSortSelection(_appearances.Sort));
    }

    [RelayCommand]
    private Task CheckForMoreVoiceActors()
        => ListOps.RunAsync(
            "Voice Actors · check for more",
            "character",
            LoadedId,
            () => _voiceActors.CheckForMoreAsync(Scope.EnsureActive()),
            () => _voiceActors.Items.Count);

    [RelayCommand]
    private void ToggleSpoilers() => IsShowingSpoilers = !IsShowingSpoilers;

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    [RelayCommand]
    private async Task NavigateToStaff(int staffId)
    {
        Logger.LogInformation("NAVTRACE Character→Staff with id={StaffId}", staffId);
        if (staffId <= 0)
        {
            return;
        }

        await NavigationService.GoToAsync("staff-details", animate: false, new Dictionary<string, object>
        {
            ["staffId"] = staffId,
        });
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private void OnAppearancesChanged()
    {
        OnPropertyChanged(nameof(HasAppearances));
        OnPropertyChanged(nameof(AppearancesBusy));
        OnPropertyChanged(nameof(AppearancesSort));
        LoadMoreAppearancesCommand.NotifyCanExecuteChanged();
    }

    private void OnVoiceActorsChanged()
    {
        OnPropertyChanged(nameof(HasVoiceActors));
        OnPropertyChanged(nameof(ShowVoiceActorsSection));
        OnPropertyChanged(nameof(VoiceActorsHasMore));
        OnPropertyChanged(nameof(IsCheckingVoiceActors));
        OnPropertyChanged(nameof(ShowVoiceActorsEmptyState));
        OnPropertyChanged(nameof(ShowVoiceActorsEndReached));
    }

    private void StampAppearanceBadges(IReadOnlyList<CharacterMediaEdge> items, string sort)
    {
        foreach (var edge in items)
        {
            edge.MetricBadge = MediaMetricBadges.ForMediaSort(edge.Node, sort);
        }
    }

    private void ResetAppearancesSortSelection() => SyncAppearancesSortSelection(AppearancesDefaultSort);

    private void SyncAppearancesSortSelection(string code)
    {
        foreach (var opt in AppearancesSortOptions)
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

    private List<string> BuildAlternativeNames()
    {
        var names = new List<string>();
        if (Character?.Name is null)
        {
            return names;
        }

        names.AddRange(Character.Name.Alternative.Where(n => !string.IsNullOrWhiteSpace(n)));

        if (IsShowingSpoilers)
        {
            names.AddRange(Character.Name.AlternativeSpoiler.Where(n => !string.IsNullOrWhiteSpace(n)));
        }

        return names;
    }
}
