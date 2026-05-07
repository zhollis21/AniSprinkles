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

    private const int InitialDisplayCount = 25;
    private const int LoadMorePageSize = 25;
    // perPage on AniList Character.media accepts up to 50; keep request count low while leaving
    // initial first paint within budget.
    private const int FetchPageSize = 50;

    private int _loadedCharacterId;
    private ParsedDescription _parsedDescription = ParsedDescription.Empty;
    private string _appearancesSort = "POPULARITY_DESC";
    private string _voiceActorsSort = "FAVOURITES_DESC";

    // After initial load, _sortedAppearances holds the entire character roster sorted client-side
    // by the active Appears In sort. DisplayedAppearances takes a prefix that grows on Load More.
    // Sort changes / Load More are pure local list ops — no API calls.
    private List<CharacterMediaEdge> _sortedAppearances = [];
    private readonly HashSet<int> _seenVoiceActorIds = [];

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
    [NotifyPropertyChangedFor(nameof(HasAppearances))]
    [NotifyPropertyChangedFor(nameof(AppearancesHasMore))]
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

    public IReadOnlyList<SortOption> VoiceActorsSortOptions { get; } =
    [
        new SortOption { Code = "FAVOURITES_DESC", Display = "Most Favorited", IsSelected = true },
        new SortOption { Code = "LANGUAGE",        Display = "Language" },
        new SortOption { Code = "NAME",            Display = "Name" },
    ];

    public bool AppearancesHasMore => DisplayedAppearances.Count < _sortedAppearances.Count;

    // The visible subset of _sortedAppearances. BindableLayout binds to this; never to Character.Media.
    public ObservableCollection<CharacterMediaEdge> DisplayedAppearances { get; } = [];

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

    public bool HasAppearances => Character?.Media is { Count: > 0 };

    public bool HasSiteUrl => !string.IsNullOrWhiteSpace(Character?.SiteUrl);

    // ObservableCollection so the BindableLayout updates incrementally instead of rebuilding
    // (which resets the horizontal scroll position when nothing actually changed).
    public ObservableCollection<VoiceActor> VoiceActors { get; } = [];

    public CharacterDetailsPageModel(
        IAniListClient aniListClient,
        INavigationService navigationService,
        ILogger<CharacterDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _navigationService = navigationService;
        _logger = logger;
    }

    partial void OnCharacterChanged(Character? value)
    {
        _parsedDescription = DescriptionParser.Parse(value?.Description);
        // Character.Media is the complete roster by the time this fires (LoadAsync eager-fetches
        // all pages first); VAs come along as a deduped sorted snapshot.
        RepopulateVoiceActorsFromCharacter();
    }

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

    public async Task LoadAsync(int characterId)
    {
        if (characterId <= 0)
        {
            ShowError("Not Found", "Invalid character id.", canRetry: false);
            return;
        }

        _loadedCharacterId = characterId;
        IsBusy = true;
        if (Character is null || Character.Id != characterId)
        {
            CurrentState = PageState.InitialLoading;
            IsShowingSpoilers = false;
            IsDescriptionExpanded = false;
        }

        try
        {
            var character = await _aniListClient.GetCharacterAsync(characterId);
            if (character is null)
            {
                ShowError("Not Found", "We couldn't find this character.", canRetry: false);
                return;
            }

            // Eager-fetch every remaining page of media in parallel before showing the page. After
            // this returns, Character.Media is the complete roster — sort changes and Load More
            // become pure local list ops, the VA section is complete from first paint, and there's
            // no leaky abstraction between Appears In paging and what shows up under Voice Actors.
            await FillRemainingAppearancesAsync(character, characterId).ConfigureAwait(true);

            // Stamp metric badges using the active sort code on the now-complete media set.
            StampAppearanceBadges(character);

            Character = character;
            ResetDisplayedAppearances();
            CurrentState = PageState.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load character {CharacterId}", characterId);
            ShowError("Something Went Wrong", "Failed to load character details.", canRetry: true, details: ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FillRemainingAppearancesAsync(Character character, int characterId)
    {
        var pageInfo = character.MediaPageInfo;
        if (pageInfo is null || pageInfo.LastPage <= 1) return;

        // Heavy CharacterQuery returns page 1; spawn parallel requests for pages 2..lastPage.
        // Order of returned items doesn't matter — we sort client-side by the active sort code.
        var tasks = new List<Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)>>();
        for (var page = 2; page <= pageInfo.LastPage; page++)
        {
            tasks.Add(_aniListClient.LoadCharacterMediaPageAsync(
                characterId, page, "POPULARITY_DESC", perPage: FetchPageSize));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(true);
        foreach (var (items, _) in results)
        {
            foreach (var item in items)
            {
                character.Media.Add(item);
            }
        }
    }

    private void StampAppearanceBadges(Character character)
    {
        foreach (var edge in character.Media)
        {
            edge.MetricBadge = BuildAppearanceMetricBadge(edge.Node, _appearancesSort);
        }
    }

    private void ResetDisplayedAppearances()
    {
        DisplayedAppearances.Clear();
        if (Character is null)
        {
            _sortedAppearances = [];
            OnPropertyChanged(nameof(AppearancesHasMore));
            return;
        }
        _sortedAppearances = SortAppearances(Character.Media, _appearancesSort);
        var initial = Math.Min(InitialDisplayCount, _sortedAppearances.Count);
        for (var i = 0; i < initial; i++)
        {
            DisplayedAppearances.Add(_sortedAppearances[i]);
        }
        OnPropertyChanged(nameof(AppearancesHasMore));
    }

    private static List<CharacterMediaEdge> SortAppearances(IEnumerable<CharacterMediaEdge> source, string sort) =>
        sort switch
        {
            "SCORE_DESC"       => source.OrderByDescending(e => e.Node?.AverageScore ?? 0).ToList(),
            "FAVOURITES_DESC"  => source.OrderByDescending(e => e.Node?.Favourites ?? 0).ToList(),
            "START_DATE_DESC"  => source.OrderByDescending(e => e.Node?.StartDate?.Year ?? 0).ToList(),
            "START_DATE"       => source.OrderBy(e => e.Node?.StartDate?.Year ?? int.MaxValue).ToList(),
            "TITLE_ROMAJI"     => source.OrderBy(e => e.Node?.Title?.Romaji ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),
            _                  => source.OrderByDescending(e => e.Node?.Popularity ?? 0).ToList(), // POPULARITY_DESC
        };

    private void AddVoiceActorIfNew(VoiceActor va)
    {
        if (!_seenVoiceActorIds.Add(va.Id)) return;
        var index = FindVoiceActorInsertIndex(va);
        VoiceActors.Insert(index, va);
    }

    private int FindVoiceActorInsertIndex(VoiceActor va)
    {
        // Linear scan — the VA list tops out at a couple hundred even for prolific characters,
        // and we only insert during the brief background accumulation window.
        for (var i = 0; i < VoiceActors.Count; i++)
        {
            if (CompareVoiceActorsForCurrentSort(va, VoiceActors[i]) < 0) return i;
        }
        return VoiceActors.Count;
    }

    private int CompareVoiceActorsForCurrentSort(VoiceActor a, VoiceActor b)
    {
        return _voiceActorsSort switch
        {
            "NAME" => string.Compare(a.Name?.Full ?? string.Empty, b.Name?.Full ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            "LANGUAGE" => CompareBy(a, b,
                static x => x.Language ?? string.Empty,
                static x => -(x.Favourites ?? 0),
                static x => x.Name?.Full ?? string.Empty),
            _ => CompareBy(a, b,
                static x => -(x.Favourites ?? 0),
                static x => x.Language ?? string.Empty,
                static x => x.Name?.Full ?? string.Empty),
        };
    }

    private static int CompareBy(VoiceActor a, VoiceActor b, params Func<VoiceActor, IComparable>[] keys)
    {
        foreach (var key in keys)
        {
            var cmp = Comparer<IComparable>.Default.Compare(key(a), key(b));
            if (cmp != 0) return cmp;
        }
        return 0;
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

    private void RepopulateVoiceActorsFromCharacter()
    {
        VoiceActors.Clear();
        _seenVoiceActorIds.Clear();
        if (Character is null) return;
        foreach (var edge in Character.Media)
        {
            foreach (var va in edge.VoiceActors)
            {
                AddVoiceActorIfNew(va);
            }
        }
    }

    private void ResortVoiceActors()
    {
        // VA sort change re-orders the same accumulated set. Clear+Add preserves contents (and
        // _seenVoiceActorIds), only horizontal scroll position resets — that's the intended UX.
        var snapshot = VoiceActors.ToList();
        snapshot.Sort(CompareVoiceActorsForCurrentSort);
        VoiceActors.Clear();
        foreach (var va in snapshot) VoiceActors.Add(va);
    }

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

    [RelayCommand]
    private void ToggleSpoilers()
    {
        IsShowingSpoilers = !IsShowingSpoilers;
    }

    [RelayCommand]
    private void ToggleDescription()
    {
        IsDescriptionExpanded = !IsDescriptionExpanded;
    }

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
    private void SelectAppearancesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == _appearancesSort || Character is null) return;

        ApplyAppearancesSortSelection(code);
        // Re-stamp badges so the icon/value reflect the new sort, then re-sort + reset visible window.
        StampAppearanceBadges(Character);
        ResetDisplayedAppearances();
    }

    private void ApplyAppearancesSortSelection(string code)
    {
        foreach (var opt in AppearancesSortOptions)
        {
            opt.IsSelected = string.Equals(opt.Code, code, StringComparison.Ordinal);
        }
        _appearancesSort = code;
    }

    [RelayCommand]
    private void LoadMoreAppearances()
    {
        if (!AppearancesHasMore) return;

        var start = DisplayedAppearances.Count;
        var end = Math.Min(start + LoadMorePageSize, _sortedAppearances.Count);
        for (var i = start; i < end; i++)
        {
            DisplayedAppearances.Add(_sortedAppearances[i]);
        }
        OnPropertyChanged(nameof(AppearancesHasMore));
    }

    [RelayCommand]
    private void SelectVoiceActorsSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == _voiceActorsSort)
        {
            return;
        }

        foreach (var opt in VoiceActorsSortOptions)
        {
            opt.IsSelected = string.Equals(opt.Code, code, StringComparison.Ordinal);
        }
        _voiceActorsSort = code;
        ResortVoiceActors();
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
}
