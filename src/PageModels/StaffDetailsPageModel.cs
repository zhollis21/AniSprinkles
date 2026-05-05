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
    private ParsedDescription _parsedDescription = ParsedDescription.Empty;
    private string _voiceRolesSort = "FAVOURITES_DESC";
    private string _productionRolesSort = "POPULARITY_DESC";

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
    [NotifyPropertyChangedFor(nameof(HasVoiceRoles))]
    [NotifyPropertyChangedFor(nameof(HasProductionRoles))]
    [NotifyPropertyChangedFor(nameof(VoiceRolesHasMore))]
    [NotifyPropertyChangedFor(nameof(ProductionRolesHasMore))]
    [NotifyPropertyChangedFor(nameof(HasSiteUrl))]
    private Staff? _staff;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BioProse))]
    [NotifyPropertyChangedFor(nameof(BioStats))]
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
    private bool _isLoadingVoiceRoles;

    [ObservableProperty]
    private bool _isLoadingProductionRoles;

    public IReadOnlyList<SortOption> VoiceRolesSortOptions { get; } =
    [
        new SortOption { Code = "FAVOURITES_DESC", Display = "Favorites", IsSelected = true },
        new SortOption { Code = "ROLE",            Display = "Role" },
        new SortOption { Code = "RELEVANCE",       Display = "Relevance" },
    ];

    public IReadOnlyList<SortOption> ProductionRolesSortOptions { get; } =
    [
        new SortOption { Code = "POPULARITY_DESC", Display = "Popularity", IsSelected = true },
        new SortOption { Code = "SCORE_DESC",      Display = "Avg Score" },
        new SortOption { Code = "FAVOURITES_DESC", Display = "Favorites" },
        new SortOption { Code = "START_DATE_DESC", Display = "Newest" },
        new SortOption { Code = "START_DATE",      Display = "Oldest" },
        new SortOption { Code = "TITLE_ROMAJI",    Display = "Title" },
    ];

    public bool VoiceRolesHasMore => Staff?.CharactersPageInfo?.HasNextPage == true;
    public bool ProductionRolesHasMore => Staff?.StaffMediaPageInfo?.HasNextPage == true;

    public bool HasStaff => Staff is not null;

    public string PageTitle => Staff?.DisplayName ?? "Staff";

    public bool HasFavourites => Staff?.Favourites is > 0;

    public string FavouritesDisplay => FormatFavourites(Staff?.Favourites);

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

    partial void OnStaffChanged(Staff? value)
    {
        _parsedDescription = DescriptionParser.Parse(value?.Description);
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
            IsDescriptionExpanded = false;
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

        // Each occupation gets its own chip — they color-code more nicely than a single joined string.
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

    private void ShowError(string title, string subtitle, bool canRetry, string details = "")
    {
        ErrorTitle = title;
        ErrorSubtitle = subtitle;
        ErrorIconGlyph = FluentIconsRegular.ErrorCircle24;
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
    private async Task SelectVoiceRolesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == _voiceRolesSort || Staff is null || IsLoadingVoiceRoles)
        {
            return;
        }

        var previous = _voiceRolesSort;
        ApplyVoiceRolesSortSelection(code);

        IsLoadingVoiceRoles = true;
        try
        {
            var (items, pageInfo) = await _aniListClient
                .LoadStaffCharactersPageAsync(_loadedStaffId, page: 1, sort: code).ConfigureAwait(true);
            Staff.Characters.Clear();
            foreach (var item in items)
            {
                Staff.Characters.Add(item);
            }
            Staff.CharactersPageInfo = pageInfo;
            OnPropertyChanged(nameof(VoiceRolesHasMore));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply Voice Roles sort {Sort} for staff {StaffId}", code, _loadedStaffId);
            // Revert chip selection so what's highlighted matches the items actually shown.
            ApplyVoiceRolesSortSelection(previous);
        }
        finally
        {
            IsLoadingVoiceRoles = false;
        }
    }

    private void ApplyVoiceRolesSortSelection(string code)
    {
        foreach (var opt in VoiceRolesSortOptions)
        {
            opt.IsSelected = string.Equals(opt.Code, code, StringComparison.Ordinal);
        }
        _voiceRolesSort = code;
    }

    [RelayCommand]
    private async Task SelectProductionRolesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == _productionRolesSort || Staff is null || IsLoadingProductionRoles)
        {
            return;
        }

        var previous = _productionRolesSort;
        ApplyProductionRolesSortSelection(code);

        IsLoadingProductionRoles = true;
        try
        {
            var (items, pageInfo) = await _aniListClient
                .LoadStaffMediaPageAsync(_loadedStaffId, page: 1, sort: code).ConfigureAwait(true);
            Staff.StaffMedia.Clear();
            foreach (var item in items)
            {
                Staff.StaffMedia.Add(item);
            }
            Staff.StaffMediaPageInfo = pageInfo;
            OnPropertyChanged(nameof(ProductionRolesHasMore));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply Production Roles sort {Sort} for staff {StaffId}", code, _loadedStaffId);
            ApplyProductionRolesSortSelection(previous);
        }
        finally
        {
            IsLoadingProductionRoles = false;
        }
    }

    private void ApplyProductionRolesSortSelection(string code)
    {
        foreach (var opt in ProductionRolesSortOptions)
        {
            opt.IsSelected = string.Equals(opt.Code, code, StringComparison.Ordinal);
        }
        _productionRolesSort = code;
    }

    [RelayCommand]
    private async Task LoadMoreVoiceRoles()
    {
        if (Staff is null || IsLoadingVoiceRoles || !VoiceRolesHasMore)
        {
            return;
        }

        IsLoadingVoiceRoles = true;
        try
        {
            var nextPage = (Staff.CharactersPageInfo?.CurrentPage ?? 1) + 1;
            var (items, pageInfo) = await _aniListClient
                .LoadStaffCharactersPageAsync(_loadedStaffId, page: nextPage, sort: _voiceRolesSort).ConfigureAwait(true);
            foreach (var item in items)
            {
                Staff.Characters.Add(item);
            }
            Staff.CharactersPageInfo = pageInfo;
            OnPropertyChanged(nameof(VoiceRolesHasMore));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load more Voice Roles for staff {StaffId}", _loadedStaffId);
        }
        finally
        {
            IsLoadingVoiceRoles = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreProductionRoles()
    {
        if (Staff is null || IsLoadingProductionRoles || !ProductionRolesHasMore)
        {
            return;
        }

        IsLoadingProductionRoles = true;
        try
        {
            var nextPage = (Staff.StaffMediaPageInfo?.CurrentPage ?? 1) + 1;
            var (items, pageInfo) = await _aniListClient
                .LoadStaffMediaPageAsync(_loadedStaffId, page: nextPage, sort: _productionRolesSort).ConfigureAwait(true);
            foreach (var item in items)
            {
                Staff.StaffMedia.Add(item);
            }
            Staff.StaffMediaPageInfo = pageInfo;
            OnPropertyChanged(nameof(ProductionRolesHasMore));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load more Production Roles for staff {StaffId}", _loadedStaffId);
        }
        finally
        {
            IsLoadingProductionRoles = false;
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

public sealed record QuickFactChip(string Display);
