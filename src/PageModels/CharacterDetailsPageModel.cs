using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging;
using Sentry;

namespace AniSprinkles.PageModels;

public partial class CharacterDetailsPageModel : ObservableObject
{
    private readonly IAniListClient _aniListClient;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IUserFeedback _feedback;
    private readonly ILogger<CharacterDetailsPageModel> _logger;
    private readonly ListOperationRunner _listOps;
    private readonly FavouriteToggleRunner _favouriteRunner;

    private const int PageSize = 25;
    private const string AppearancesDefaultSort = "POPULARITY_DESC";

    // The fixed media ordering the Voice Actors list walks. Kept independent of the Appears In sort
    // so changing that sort never disturbs the voice-actor list (a UX bug in the prior design).
    private const string VoiceActorMediaSort = "POPULARITY_DESC";

    private int _loadedCharacterId;
    private ParsedDescription _parsedDescription = ParsedDescription.Empty;
    private readonly PageLoadScope _scope = new();

    // The Appears In list and the deduped Voice Actors list are two fully independent views over
    // Character.media. Each owns its cursor; neither mutates the other.
    private readonly PaginatedSection<CharacterMediaEdge> _appearances;
    private readonly VoiceActorAggregator _voiceActors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.InitialLoading;

    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacter))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
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
        ILogger<CharacterDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _navigationService = navigationService;
        _feedback = feedback;
        _logger = logger;
        _listOps = new ListOperationRunner(logger, feedback);
        _favouriteRunner = new FavouriteToggleRunner(aniListClient, feedback, logger);

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

    public bool HasFavourites => Character?.Favourites is > 0;

    public string FavouritesDisplay => FormatFavourites(Character?.Favourites);

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

    public bool HasSiteUrl => !string.IsNullOrWhiteSpace(Character?.SiteUrl);

    public bool IsAuthenticated { get; private set; }

    /// <summary>Viewer's favorite state for this character; drives the heart fill on the favourites stat.</summary>
    public bool IsFavourite => Character?.IsFavourite ?? false;

    public bool CanToggleFavourite => IsAuthenticated && !_favouriteRunner.IsBusy && Character is not null;

    partial void OnCharacterChanged(Character? value)
    {
        _parsedDescription = DescriptionParser.Parse(value?.Description);
        OnPropertyChanged(nameof(IsFavourite));
        ToggleFavouriteCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentStateChanged(PageState oldValue, PageState newValue)
        => _logger.LogInformation("PageState: {OldState} → {NewState} (key={StateKey})", oldValue, newValue, CurrentStateKey ?? "(null)");

    // ---- Load -----------------------------------------------------------------------------------

    public async Task LoadAsync(int characterId)
    {
        if (characterId <= 0)
        {
            ShowError("Not Found", "Invalid character id.", canRetry: false);
            return;
        }

        // Same character already loaded: keep its sections + sort and just restore Content state. This is
        // hit when returning from a pushed sub-page (e.g. a voice actor's staff page) and — importantly —
        // when a CommunityToolkit sort popup closes (it fires the host page's OnAppearing → reload). Without
        // this guard the popup would reset the sort the user just picked. Mirrors MediaDetailsPageModel.
        if (Character is not null && Character.Id == characterId)
        {
            CurrentState = PageState.Content;
            return;
        }

        _loadedCharacterId = characterId;
        var token = _scope.Begin(); // fresh page scope; OnDisappearing cancels it on navigate-away



        IsBusy = true;
        if (Character is null || Character.Id != characterId)
        {
            CurrentState = PageState.InitialLoading;
            IsShowingSpoilers = false;
            IsDescriptionExpanded = false;
        }

        // Clear any state from a previously-loaded character before fetching the new one.
        _appearances.Reset();
        _voiceActors.Reset();
        ResetAppearancesSortSelection();

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("NAVTRACE CharacterDetails load start (character {CharacterId})", characterId);

        try
        {
            IsAuthenticated = !string.IsNullOrWhiteSpace(await _authService.GetAccessTokenAsync(token).ConfigureAwait(true));
            ToggleFavouriteCommand.NotifyCanExecuteChanged();

            var character = await _aniListClient.GetCharacterAsync(characterId, cancellationToken: token).ConfigureAwait(true);
            if (character is null)
            {
                _logger.LogInformation("NAVTRACE CharacterDetails not found in {ElapsedMs}ms (character {CharacterId})", stopwatch.ElapsedMilliseconds, characterId);
                ShowError("Not Found", "We couldn't find this character.", canRetry: false);
                return;
            }

            Character = character;

            // Seed both independent sections from the single heavy first-page query.
            var pageOne = character.Media.ToList();
            _appearances.Seed(pageOne, character.MediaPageInfo);
            _voiceActors.Seed(pageOne, character.MediaPageInfo);

            CurrentState = PageState.Content;
            _logger.LogInformation(
                "NAVTRACE CharacterDetails fetch+seed in {ElapsedMs}ms (character {CharacterId}, {Appearances} appearances, {VoiceActors} VAs); UI render follows",
                stopwatch.ElapsedMilliseconds, characterId, _appearances.Items.Count, _voiceActors.Items.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("NAVTRACE CharacterDetails load cancelled after {ElapsedMs}ms (character {CharacterId})", stopwatch.ElapsedMilliseconds, characterId);
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;
            var isNotFound = apiEx?.Kind == ApiErrorKind.NotFound;
            if (isNotFound)
            {
                // NotFound is non-retryable and intentionally kept out of Sentry — log at Warning so it stays a breadcrumb.
                _logger.LogWarning(ex, "NAVTRACE CharacterDetails not found on AniList in {ElapsedMs}ms (character {CharacterId})", stopwatch.ElapsedMilliseconds, characterId);
            }
            else
            {
                _logger.LogError(ex, "NAVTRACE CharacterDetails load failed in {ElapsedMs}ms (character {CharacterId})", stopwatch.ElapsedMilliseconds, characterId);
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

    private Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> FetchAppearancesPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => _aniListClient.LoadCharacterMediaPageAsync(_loadedCharacterId, page, sort, PageSize, cancellationToken);

    private Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> FetchVoiceActorMediaPageAsync(
        int page, CancellationToken cancellationToken)
        => _aniListClient.LoadCharacterMediaPageAsync(_loadedCharacterId, page, VoiceActorMediaSort, PageSize, cancellationToken);

    // ---- Commands -------------------------------------------------------------------------------

    // CanExecute gates the scroll-threshold trigger: with no next page or while a fetch/sort is in
    // flight, the CollectionView's RemainingItemsThresholdReached can't re-invoke this (which would
    // otherwise log a no-op LISTTRACE pair on every scroll-to-end). LoadMoreAsync stays guarded too.
    [RelayCommand(CanExecute = nameof(CanLoadMoreAppearances))]
    private Task LoadMoreAppearances()
        => _listOps.RunAsync(
            "Appears In · Load More",
            "character",
            _loadedCharacterId,
            () => _appearances.LoadMoreAsync(_scope.EnsureActive()),
            () => _appearances.Items.Count);

    private bool CanLoadMoreAppearances() => _appearances.CanLoadMore;

    [RelayCommand]
    private Task SelectAppearancesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, _appearances.Sort, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return _listOps.RunAsync(
            $"Appears In · sort→{code}",
            "character",
            _loadedCharacterId,
            () => _appearances.ChangeSortAsync(code, _scope.EnsureActive()),
            () => _appearances.Items.Count,
            // Keep the chip selection in sync with the sort that actually took effect.
            onComplete: () => SyncAppearancesSortSelection(_appearances.Sort));
    }

    [RelayCommand]
    private Task CheckForMoreVoiceActors()
        => _listOps.RunAsync(
            "Voice Actors · check for more",
            "character",
            _loadedCharacterId,
            () => _voiceActors.CheckForMoreAsync(_scope.EnsureActive()),
            () => _voiceActors.Items.Count);

    [RelayCommand]
    private void ToggleSpoilers() => IsShowingSpoilers = !IsShowingSpoilers;

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    [RelayCommand]
    private async Task OpenSiteUrl()
    {
        if (string.IsNullOrWhiteSpace(Character?.SiteUrl))
        {
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(new Uri(Character.SiteUrl), BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open AniList character URL");
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleFavourite))]
    private async Task ToggleFavourite()
    {
        var character = Character;
        // Re-check the gate here (not just via the command's CanExecute) so the failure-snackbar
        // Retry can't run an optimistic flip if auth/busy state changed since the failure.
        if (character is null || !CanToggleFavourite)
        {
            return;
        }

        if (await _favouriteRunner.ToggleAsync(character, FavouriteKind.Character, NotifyFavouriteChanged, () => _ = ToggleFavourite()))
        {
            SentrySdk.AddBreadcrumb($"Favourite toggled (character {character.Id} → {(character.IsFavourite ? "on" : "off")})", "list", "user");
        }
    }

    private void NotifyFavouriteChanged()
    {
        OnPropertyChanged(nameof(IsFavourite));
        OnPropertyChanged(nameof(FavouritesDisplay));
        OnPropertyChanged(nameof(HasFavourites));
        ToggleFavouriteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private Task RetryLoad() => LoadAsync(_loadedCharacterId);

    [RelayCommand]
    private async Task NavigateToStaff(int staffId)
    {
        _logger.LogInformation("NAVTRACE Character→Staff with id={StaffId}", staffId);
        if (staffId <= 0)
        {
            return;
        }

        await _navigationService.GoToAsync("staff-details", animate: false, new Dictionary<string, object>
        {
            ["staffId"] = staffId,
        });
    }

    [RelayCommand]
    private async Task NavigateToMedia(RelatedMedia? media)
    {
        var mediaId = media?.Id ?? 0;
        _logger.LogInformation("NAVTRACE Character→Media with id={MediaId}", mediaId);
        if (mediaId <= 0)
        {
            return;
        }

        // Detail screen is anime-only (Media(id:, type: ANIME)); a manga/novel id would 404.
        // Character "Appears In" media can include manga, so toast instead of navigating.
        if (media is { IsAnime: false })
        {
            _logger.LogInformation("NAVTRACE Character→Media skipped non-anime {MediaId} (type={Type}).", mediaId, media.Type);
            await _feedback.ShowToastAsync("Manga & Novel details aren't supported yet.");
            return;
        }

        await _navigationService.GoToAsync("media-details", animate: false, new Dictionary<string, object>
        {
            ["mediaId"] = mediaId,
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

    private static (string Title, string Subtitle) DescribeError(Exception ex)
        => ex is AniListApiException apiEx
            ? (apiEx.UserTitle, apiEx.UserSubtitle)
            : ("Something Went Wrong", "Failed to load character details.");

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

    // iconGlyph lets the catch path surface a classified AniListApiException.IconGlyph (e.g. NotFound
    // → DismissCircle24); the static "invalid id" / "couldn't find" callers fall back to ErrorCircle24.
    private void ShowError(string title, string subtitle, bool canRetry, string details = "", string? iconGlyph = null)
    {
        ErrorTitle = title;
        ErrorSubtitle = subtitle;
        ErrorIconGlyph = iconGlyph ?? FluentIconsRegular.ErrorCircle24;
        ErrorDetails = details;
        CanRetry = canRetry;
        CurrentState = PageState.Error;
    }

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
