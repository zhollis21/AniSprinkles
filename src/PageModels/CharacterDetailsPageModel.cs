using System.Collections.ObjectModel;
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
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(HasSpoilers))]
    [NotifyPropertyChangedFor(nameof(BodyText))]
    [NotifyPropertyChangedFor(nameof(QuickFactsChips))]
    [NotifyPropertyChangedFor(nameof(HasQuickFacts))]
    [NotifyPropertyChangedFor(nameof(AlternativeNames))]
    [NotifyPropertyChangedFor(nameof(HasAlternativeNames))]
    [NotifyPropertyChangedFor(nameof(HasAppearances))]
    [NotifyPropertyChangedFor(nameof(HasSiteUrl))]
    private Character? _character;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyText))]
    [NotifyPropertyChangedFor(nameof(SpoilerToggleLabel))]
    [NotifyPropertyChangedFor(nameof(AlternativeNames))]
    private bool _isShowingSpoilers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredVoiceActors))]
    private string _selectedLanguage = string.Empty;

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

    public ObservableCollection<LanguageChip> LanguageChips { get; } = [];

    public bool HasLanguageChoices => LanguageChips.Count > 1;

    public bool HasCharacter => Character is not null;

    public string PageTitle => Character?.DisplayName ?? "Character";

    public bool HasFavourites => Character?.Favourites is > 0;

    public string FavouritesDisplay => FormatFavourites(Character?.Favourites);

    public bool IsBirthdayToday => BirthdayChecker.IsBirthdayToday(Character?.DateOfBirth, DateTime.Today);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Character?.Description);

    public bool HasSpoilers => SpoilerHtmlProcessor.ContainsSpoilers(Character?.Description)
        || (Character?.Name?.AlternativeSpoiler is { Count: > 0 });

    public string BodyText => SpoilerHtmlProcessor.Process(Character?.Description, IsShowingSpoilers);

    public string SpoilerToggleLabel => IsShowingSpoilers ? "👁  Hide spoilers" : "👁  Reveal spoilers";

    public IReadOnlyList<string> QuickFactsChips => BuildQuickFacts();

    public bool HasQuickFacts => QuickFactsChips.Count > 0;

    public IReadOnlyList<string> AlternativeNames => BuildAlternativeNames();

    public bool HasAlternativeNames => AlternativeNames.Count > 0;

    public bool HasAppearances => Character?.Media is { Count: > 0 };

    public bool HasSiteUrl => !string.IsNullOrWhiteSpace(Character?.SiteUrl);

    public IReadOnlyList<VoiceActor> FilteredVoiceActors => BuildFilteredVoiceActors();

    public CharacterDetailsPageModel(
        IAniListClient aniListClient,
        INavigationService navigationService,
        ILogger<CharacterDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _navigationService = navigationService;
        _logger = logger;
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
            BuildLanguageChips();
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

    private void BuildLanguageChips()
    {
        LanguageChips.Clear();
        if (Character is null)
        {
            return;
        }

        var languages = Character.Media
            .SelectMany(m => m.VoiceActors)
            .Select(va => va.Language)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // "All" chip first, selected by default.
        LanguageChips.Add(new LanguageChip { Code = string.Empty, Display = "All", IsSelected = true });
        foreach (var lang in languages)
        {
            LanguageChips.Add(new LanguageChip { Code = lang!, Display = lang! });
        }

        SelectedLanguage = string.Empty;
        OnPropertyChanged(nameof(HasLanguageChoices));
    }

    [RelayCommand]
    private void SelectLanguage(string? code)
    {
        var normalized = code ?? string.Empty;
        foreach (var chip in LanguageChips)
        {
            chip.IsSelected = string.Equals(chip.Code, normalized, StringComparison.OrdinalIgnoreCase);
        }
        SelectedLanguage = normalized;
    }

    private List<VoiceActor> BuildFilteredVoiceActors()
    {
        if (Character is null)
        {
            return [];
        }

        var seen = new HashSet<int>();
        var result = new List<VoiceActor>();
        foreach (var edge in Character.Media)
        {
            foreach (var va in edge.VoiceActors)
            {
                if (!seen.Add(va.Id))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(SelectedLanguage)
                    || string.Equals(va.Language, SelectedLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(va);
                }
            }
        }

        return result;
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

    private List<string> BuildQuickFacts()
    {
        var chips = new List<string>();
        if (Character is null)
        {
            return chips;
        }

        if (!string.IsNullOrWhiteSpace(Character.Gender))
        {
            chips.Add(Character.Gender);
        }

        if (!string.IsNullOrWhiteSpace(Character.Age))
        {
            chips.Add($"Age {Character.Age}");
        }

        var dob = FuzzyDateFormatter.Format(Character.DateOfBirth, includeYear: false);
        if (dob is not null)
        {
            chips.Add($"Born {dob}");
        }

        if (!string.IsNullOrWhiteSpace(Character.BloodType))
        {
            chips.Add($"Blood type {Character.BloodType}");
        }

        return chips;
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
}
