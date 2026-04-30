using System.Globalization;
using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

public partial class StaffDetailsPageModel : ObservableObject
{
    private readonly IAniListClient _aniListClient;
    private readonly INavigationService _navigationService;
    private readonly ILogger<StaffDetailsPageModel> _logger;

    private int _loadedStaffId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.InitialLoading;

    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStaff))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(FavouritesDisplay))]
    [NotifyPropertyChangedFor(nameof(HasFavourites))]
    [NotifyPropertyChangedFor(nameof(IsBirthdayToday))]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(HasSpoilers))]
    [NotifyPropertyChangedFor(nameof(BodyText))]
    [NotifyPropertyChangedFor(nameof(QuickFactsChips))]
    [NotifyPropertyChangedFor(nameof(HasQuickFacts))]
    [NotifyPropertyChangedFor(nameof(HasVoiceRoles))]
    [NotifyPropertyChangedFor(nameof(HasProductionRoles))]
    [NotifyPropertyChangedFor(nameof(HasSiteUrl))]
    private Staff? _staff;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyText))]
    [NotifyPropertyChangedFor(nameof(SpoilerToggleLabel))]
    private bool _isShowingSpoilers;

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

    public bool HasStaff => Staff is not null;

    public string PageTitle => Staff?.DisplayName ?? "Staff";

    public bool HasFavourites => Staff?.Favourites is > 0;

    public string FavouritesDisplay => FormatFavourites(Staff?.Favourites);

    public bool IsBirthdayToday => BirthdayChecker.IsBirthdayToday(Staff?.DateOfBirth, DateTime.Today);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Staff?.Description);

    public bool HasSpoilers => SpoilerHtmlProcessor.ContainsSpoilers(Staff?.Description);

    public string BodyText => SpoilerHtmlProcessor.Process(Staff?.Description, IsShowingSpoilers);

    public string SpoilerToggleLabel => IsShowingSpoilers ? "👁  Hide spoilers" : "👁  Reveal spoilers";

    public IReadOnlyList<string> QuickFactsChips => BuildQuickFacts();

    public bool HasQuickFacts => QuickFactsChips.Count > 0;

    public bool HasVoiceRoles => Staff?.Characters is { Count: > 0 };

    public bool HasProductionRoles => Staff?.StaffMedia is { Count: > 0 };

    public bool HasSiteUrl => !string.IsNullOrWhiteSpace(Staff?.SiteUrl);

    public StaffDetailsPageModel(
        IAniListClient aniListClient,
        INavigationService navigationService,
        ILogger<StaffDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _navigationService = navigationService;
        _logger = logger;
    }

    public async Task LoadAsync(int staffId)
    {
        if (staffId <= 0)
        {
            ShowError("Not Found", "Invalid staff id.", canRetry: false);
            return;
        }

        _loadedStaffId = staffId;
        IsBusy = true;
        if (Staff is null || Staff.Id != staffId)
        {
            CurrentState = PageState.InitialLoading;
            IsShowingSpoilers = false;
        }

        try
        {
            var staff = await _aniListClient.GetStaffAsync(staffId);
            if (staff is null)
            {
                ShowError("Not Found", "We couldn't find this staff member.", canRetry: false);
                return;
            }

            Staff = staff;
            CurrentState = PageState.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load staff {StaffId}", staffId);
            ShowError("Something Went Wrong", "Failed to load staff details.", canRetry: true, details: ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
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
        if (Staff is null)
        {
            return chips;
        }

        if (Staff.PrimaryOccupations is { Count: > 0 })
        {
            chips.Add(string.Join(" · ", Staff.PrimaryOccupations));
        }

        var yearsActive = YearsActiveFormatter.Format(Staff.YearsActive, Staff.DateOfDeath);
        if (yearsActive is not null)
        {
            chips.Add(yearsActive);
        }

        if (!string.IsNullOrWhiteSpace(Staff.HomeTown))
        {
            chips.Add(Staff.HomeTown);
        }

        // Suppress year on the DOB chip — the years-active chip already conveys lifespan.
        var dob = FuzzyDateFormatter.Format(Staff.DateOfBirth, includeYear: false);
        if (dob is not null)
        {
            chips.Add($"Born {dob}");
        }

        if (Staff.Age is > 0 && Staff.DateOfDeath is null)
        {
            chips.Add($"Age {Staff.Age}");
        }

        if (!string.IsNullOrWhiteSpace(Staff.LanguageV2))
        {
            chips.Add(Staff.LanguageV2);
        }

        return chips;
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
        if (string.IsNullOrWhiteSpace(Staff?.SiteUrl))
        {
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(new Uri(Staff.SiteUrl), BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open AniList staff URL");
        }
    }

    [RelayCommand]
    private Task RetryLoad() => LoadAsync(_loadedStaffId);

    [RelayCommand]
    private async Task NavigateToCharacter(int characterId)
    {
        _logger.LogInformation("NAVTRACE Staff→Character with id={CharacterId}", characterId);
        if (characterId <= 0)
        {
            return;
        }

        await _navigationService.GoToAsync("character-details", animate: false, new Dictionary<string, object>
        {
            ["characterId"] = characterId,
        });
    }

    [RelayCommand]
    private async Task NavigateToMedia(int mediaId)
    {
        _logger.LogInformation("NAVTRACE Staff→Media with id={MediaId}", mediaId);
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
