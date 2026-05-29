using System.Collections.ObjectModel;
using System.Globalization;
using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.PageModels;

public partial class CharacterDetailsPageModel : ObservableObject
{
    private readonly IAniListClient _aniListClient;
    private readonly INavigationService _navigationService;
    private readonly ILogger<CharacterDetailsPageModel> _logger;

    private const int PageSize = 25;
    private const string AppearancesDefaultSort = "POPULARITY_DESC";

    // The fixed media ordering the Voice Actors list walks. Kept independent of the Appears In sort
    // so changing that sort never disturbs the voice-actor list (a UX bug in the prior design).
    private const string VoiceActorMediaSort = "POPULARITY_DESC";

    private int _loadedCharacterId;
    private ParsedDescription _parsedDescription = ParsedDescription.Empty;
    private CancellationTokenSource? _pageCts;

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
        INavigationService navigationService,
        ILogger<CharacterDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _navigationService = navigationService;
        _logger = logger;

        _appearances = new PaginatedSection<CharacterMediaEdge>(
            AppearancesDefaultSort,
            FetchAppearancesPageAsync,
            edge => edge.Node?.Id ?? 0,
            StampAppearanceBadges);
        _appearances.Changed += OnAppearancesChanged;

        _voiceActors = new VoiceActorAggregator(FetchVoiceActorMediaPageAsync);
        _voiceActors.Changed += OnVoiceActorsChanged;
    }

    // ---- Appears In -----------------------------------------------------------------------------

    // Bind XAML to these; the collection instances are stable for the page model's life.
    public ObservableCollection<CharacterMediaEdge> DisplayedAppearances => _appearances.Items;

    public bool HasAppearances => _appearances.Items.Count > 0;

    public bool AppearancesHasMore => _appearances.HasNextPage;

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

    partial void OnCharacterChanged(Character? value)
    {
        _parsedDescription = DescriptionParser.Parse(value?.Description);
    }

    // ---- Load -----------------------------------------------------------------------------------

    public async Task LoadAsync(int characterId)
    {
        if (characterId <= 0)
        {
            ShowError("Not Found", "Invalid character id.", canRetry: false);
            return;
        }

        _loadedCharacterId = characterId;
        StartNewPageScope();
        var token = _pageCts!.Token; // StartNewPageScope just assigned a fresh CTS



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

        try
        {
            var character = await _aniListClient.GetCharacterAsync(characterId, cancellationToken: token).ConfigureAwait(true);
            if (character is null)
            {
                ShowError("Not Found", "We couldn't find this character.", canRetry: false);
                return;
            }

            Character = character;

            // Seed both independent sections from the single heavy first-page query.
            var pageOne = character.Media.ToList();
            _appearances.Seed(pageOne, character.MediaPageInfo);
            _voiceActors.Seed(pageOne, character.MediaPageInfo);

            CurrentState = PageState.Content;
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-load — nothing to show.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load character {CharacterId}", characterId);
            var (title, subtitle) = DescribeError(ex);
            ShowError(title, subtitle, canRetry: true, details: ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CancelInFlight() => _pageCts?.Cancel();

    private void StartNewPageScope()
    {
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = new CancellationTokenSource();
    }

    private Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> FetchAppearancesPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => _aniListClient.LoadCharacterMediaPageAsync(_loadedCharacterId, page, sort, PageSize, cancellationToken);

    private Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> FetchVoiceActorMediaPageAsync(
        int page, CancellationToken cancellationToken)
        => _aniListClient.LoadCharacterMediaPageAsync(_loadedCharacterId, page, VoiceActorMediaSort, PageSize, cancellationToken);

    // ---- Commands -------------------------------------------------------------------------------

    [RelayCommand]
    private async Task LoadMoreAppearances()
    {
        try
        {
            await _appearances.LoadMoreAsync(_pageCts?.Token ?? CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Leave the Load More affordance in place so the user can simply try again.
            _logger.LogWarning(ex, "Load more appearances failed for character {CharacterId}", _loadedCharacterId);
        }
    }

    [RelayCommand]
    private async Task SelectAppearancesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, _appearances.Sort, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await _appearances.ChangeSortAsync(code, _pageCts?.Token ?? CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Appearances sort change failed for character {CharacterId}", _loadedCharacterId);
        }
        finally
        {
            // Keep the chip selection in sync with the sort that actually took effect.
            SyncAppearancesSortSelection(_appearances.Sort);
        }
    }

    [RelayCommand]
    private async Task CheckForMoreVoiceActors()
    {
        try
        {
            await _voiceActors.CheckForMoreAsync(_pageCts?.Token ?? CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Check for more voice actors failed for character {CharacterId}", _loadedCharacterId);
        }
    }

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
    private async Task NavigateToMedia(int mediaId)
    {
        _logger.LogInformation("NAVTRACE Character→Media with id={MediaId}", mediaId);
        if (mediaId <= 0)
        {
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
        OnPropertyChanged(nameof(AppearancesHasMore));
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
            edge.MetricBadge = BuildAppearanceMetricBadge(edge.Node, sort);
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

    private static ItemMetricBadge? BuildAppearanceMetricBadge(RelatedMedia? media, string sort)
    {
        if (media is null)
        {
            return null;
        }

        return sort switch
        {
            "POPULARITY_DESC" when media.HasPopularity => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.People24,
                IconColor = Color.FromArgb("#FF9500"),
                Text = media.PopularityDisplay,
            },
            "SCORE_DESC" when media.HasScore => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Star24,
                IconColor = Color.FromArgb("#FFCC00"),
                Text = media.ScoreDisplay,
            },
            "FAVOURITES_DESC" when media.HasFavourites => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Heart24,
                IconColor = Color.FromArgb("#FF2D95"),
                Text = media.FavouritesDisplay,
            },
            "START_DATE_DESC" or "START_DATE" when media.HasYear => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Calendar24,
                IconColor = Color.FromArgb("#00C2FF"),
                Text = media.YearDisplay,
            },
            _ => null,
        };
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

    private void ShowError(string title, string subtitle, bool canRetry, string details = "")
    {
        ErrorTitle = title;
        ErrorSubtitle = subtitle;
        ErrorIconGlyph = FluentIconsRegular.ErrorCircle24;
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
