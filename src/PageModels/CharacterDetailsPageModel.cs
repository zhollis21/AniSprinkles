using System.Globalization;
using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

public partial class CharacterDetailsPageModel : ObservableObject
{
    private readonly IAniListClient _aniListClient;
    private readonly INavigationService _navigationService;
    private readonly ILogger<CharacterDetailsPageModel> _logger;

    private int _loadedCharacterId;
    private ParsedDescription _parsedDescription = ParsedDescription.Empty;
    private string _appearancesSort = "POPULARITY_DESC";
    private string _voiceActorsSort = "LANGUAGE";

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

    [ObservableProperty]
    private bool _isLoadingAppearances;

    public IReadOnlyList<SortOption> AppearancesSortOptions { get; } =
    [
        new SortOption { Code = "POPULARITY_DESC", Display = "Popularity", IsSelected = true },
        new SortOption { Code = "SCORE_DESC",      Display = "Avg Score" },
        new SortOption { Code = "FAVOURITES_DESC", Display = "Favorites" },
        new SortOption { Code = "START_DATE_DESC", Display = "Newest" },
        new SortOption { Code = "START_DATE",      Display = "Oldest" },
        new SortOption { Code = "TITLE_ROMAJI",    Display = "Title" },
    ];

    public IReadOnlyList<SortOption> VoiceActorsSortOptions { get; } =
    [
        new SortOption { Code = "LANGUAGE", Display = "Language", IsSelected = true },
        new SortOption { Code = "NAME",     Display = "Name" },
    ];

    public bool AppearancesHasMore => Character?.MediaPageInfo?.HasNextPage == true;

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

    public IReadOnlyList<VoiceActor> VoiceActors => BuildVoiceActors();

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

            Character = character;
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

    private List<VoiceActor> BuildVoiceActors()
    {
        if (Character is null)
        {
            return [];
        }

        // Voice actors are returned nested inside media edges with no AniList sort that survives
        // the dedup pass — sort applies client-side on the deduped list (LANGUAGE groups by
        // language so users can scan to a region without an explicit filter).
        var seen = new HashSet<int>();
        var result = new List<VoiceActor>();
        foreach (var edge in Character.Media)
        {
            foreach (var va in edge.VoiceActors)
            {
                if (seen.Add(va.Id))
                {
                    result.Add(va);
                }
            }
        }

        return _voiceActorsSort switch
        {
            "NAME" => result.OrderBy(va => va.Name?.Full ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),
            _     => result.OrderBy(va => va.Language ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(va => va.Name?.Full ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                           .ToList(),
        };
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
    private async Task SelectAppearancesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == _appearancesSort || Character is null || IsLoadingAppearances)
        {
            return;
        }

        var previous = _appearancesSort;
        ApplyAppearancesSortSelection(code);

        IsLoadingAppearances = true;
        try
        {
            var (items, pageInfo) = await _aniListClient
                .LoadCharacterMediaPageAsync(_loadedCharacterId, page: 1, sort: code).ConfigureAwait(true);
            Character.Media.Clear();
            foreach (var item in items)
            {
                Character.Media.Add(item);
            }
            Character.MediaPageInfo = pageInfo;
            OnPropertyChanged(nameof(VoiceActors));
            OnPropertyChanged(nameof(AppearancesHasMore));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply Appearances sort {Sort} for character {CharacterId}", code, _loadedCharacterId);
            // Revert chip selection so what's highlighted matches the items actually shown.
            ApplyAppearancesSortSelection(previous);
        }
        finally
        {
            IsLoadingAppearances = false;
        }
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
    private async Task LoadMoreAppearances()
    {
        if (Character is null || IsLoadingAppearances || !AppearancesHasMore)
        {
            return;
        }

        IsLoadingAppearances = true;
        try
        {
            var nextPage = (Character.MediaPageInfo?.CurrentPage ?? 1) + 1;
            var (items, pageInfo) = await _aniListClient
                .LoadCharacterMediaPageAsync(_loadedCharacterId, page: nextPage, sort: _appearancesSort).ConfigureAwait(true);
            foreach (var item in items)
            {
                Character.Media.Add(item);
            }
            Character.MediaPageInfo = pageInfo;
            OnPropertyChanged(nameof(VoiceActors));
            OnPropertyChanged(nameof(AppearancesHasMore));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load more Appearances for character {CharacterId}", _loadedCharacterId);
        }
        finally
        {
            IsLoadingAppearances = false;
        }
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
        OnPropertyChanged(nameof(VoiceActors));
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
