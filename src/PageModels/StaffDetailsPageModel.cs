using System.Collections.ObjectModel;
using System.Globalization;
using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.PageModels;

public partial class StaffDetailsPageModel : ObservableObject
{
    private readonly IAniListClient _aniListClient;
    private readonly INavigationService _navigationService;
    private readonly ILogger<StaffDetailsPageModel> _logger;

    private const int InitialDisplayCount = 25;
    private const int LoadMorePageSize = 25;
    private const int FetchPageSize = 50;

    private int _loadedStaffId;
    private ParsedDescription _parsedDescription = ParsedDescription.Empty;
    private string _voiceRolesSort = "FAVOURITES_DESC";
    private string _productionRolesSort = "POPULARITY_DESC";

    // Eager-fetched complete rosters held in sorted form for the active sort. The Displayed*
    // ObservableCollections expose a prefix that grows on Load More. Sort changes / Load More
    // are pure local list ops — no API calls.
    private List<StaffCharacterEdge> _sortedVoiceRoles = [];
    private List<StaffMediaEdge> _sortedProductionRoles = [];

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


    public IReadOnlyList<SortOption> VoiceRolesSortOptions { get; } =
    [
        new SortOption { Code = "FAVOURITES_DESC", Display = "Most Favorited", IsSelected = true },
        new SortOption { Code = "ROLE",            Display = "Role" },
        new SortOption { Code = "RELEVANCE",       Display = "Relevance" },
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

    public bool VoiceRolesHasMore => DisplayedVoiceRoles.Count < _sortedVoiceRoles.Count;
    public bool ProductionRolesHasMore => DisplayedProductionRoles.Count < _sortedProductionRoles.Count;

    public ObservableCollection<StaffCharacterEdge> DisplayedVoiceRoles { get; } = [];
    public ObservableCollection<StaffMediaEdge> DisplayedProductionRoles { get; } = [];

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
        // Staff.Characters and Staff.StaffMedia are the complete rosters by the time this fires;
        // LoadAsync eager-fetches all pages up front. Badge stamping and Displayed* population
        // happen explicitly in LoadAsync after the eager fetch completes.
    }

    private static ItemMetricBadge? BuildProductionMetricBadge(RelatedMedia? media, string sort)
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

            // Eager-fetch every remaining page of BOTH Voice Roles and Production Roles in
            // parallel, so sort/Load More across both sections become pure local list ops.
            await Task.WhenAll(
                FillRemainingVoiceRolesAsync(staff, staffId),
                FillRemainingProductionRolesAsync(staff, staffId)
            ).ConfigureAwait(true);

            StampProductionBadges(staff);

            Staff = staff;
            ResetDisplayedVoiceRoles();
            ResetDisplayedProductionRoles();
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

    private async Task FillRemainingVoiceRolesAsync(Staff staff, int staffId)
    {
        var pageInfo = staff.CharactersPageInfo;
        if (pageInfo is null || pageInfo.LastPage <= 1) return;

        var tasks = new List<Task<(IReadOnlyList<StaffCharacterEdge> Items, PageInfo? PageInfo)>>();
        for (var page = 2; page <= pageInfo.LastPage; page++)
        {
            tasks.Add(_aniListClient.LoadStaffCharactersPageAsync(
                staffId, page, "FAVOURITES_DESC", perPage: FetchPageSize));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(true);
        foreach (var (items, _) in results)
        {
            foreach (var item in items)
            {
                staff.Characters.Add(item);
            }
        }
    }

    private async Task FillRemainingProductionRolesAsync(Staff staff, int staffId)
    {
        var pageInfo = staff.StaffMediaPageInfo;
        if (pageInfo is null || pageInfo.LastPage <= 1) return;

        var tasks = new List<Task<(IReadOnlyList<StaffMediaEdge> Items, PageInfo? PageInfo)>>();
        for (var page = 2; page <= pageInfo.LastPage; page++)
        {
            tasks.Add(_aniListClient.LoadStaffMediaPageAsync(
                staffId, page, "POPULARITY_DESC", perPage: FetchPageSize));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(true);
        foreach (var (items, _) in results)
        {
            foreach (var item in items)
            {
                staff.StaffMedia.Add(item);
            }
        }
    }

    private void StampProductionBadges(Staff staff)
    {
        foreach (var edge in staff.StaffMedia)
        {
            edge.MetricBadge = BuildProductionMetricBadge(edge.Node, _productionRolesSort);
        }
    }

    private void ResetDisplayedVoiceRoles()
    {
        DisplayedVoiceRoles.Clear();
        if (Staff is null)
        {
            _sortedVoiceRoles = [];
            OnPropertyChanged(nameof(VoiceRolesHasMore));
            return;
        }
        _sortedVoiceRoles = SortVoiceRoles(Staff.Characters, _voiceRolesSort);
        var initial = Math.Min(InitialDisplayCount, _sortedVoiceRoles.Count);
        for (var i = 0; i < initial; i++)
        {
            DisplayedVoiceRoles.Add(_sortedVoiceRoles[i]);
        }
        OnPropertyChanged(nameof(VoiceRolesHasMore));
    }

    private void ResetDisplayedProductionRoles()
    {
        DisplayedProductionRoles.Clear();
        if (Staff is null)
        {
            _sortedProductionRoles = [];
            OnPropertyChanged(nameof(ProductionRolesHasMore));
            return;
        }
        _sortedProductionRoles = SortProductionRoles(Staff.StaffMedia, _productionRolesSort);
        var initial = Math.Min(InitialDisplayCount, _sortedProductionRoles.Count);
        for (var i = 0; i < initial; i++)
        {
            DisplayedProductionRoles.Add(_sortedProductionRoles[i]);
        }
        OnPropertyChanged(nameof(ProductionRolesHasMore));
    }

    private static List<StaffCharacterEdge> SortVoiceRoles(IEnumerable<StaffCharacterEdge> source, string sort) =>
        sort switch
        {
            "ROLE"      => source.OrderBy(e => RolePriority(e.Role)).ToList(),
            "RELEVANCE" => source.ToList(), // server returned RELEVANCE order; preserve it
            _           => source.OrderByDescending(e => e.Node?.Favourites ?? 0).ToList(),
        };

    private static int RolePriority(string? role) => role switch
    {
        "MAIN"       => 0,
        "SUPPORTING" => 1,
        "BACKGROUND" => 2,
        _            => 3,
    };

    private static List<StaffMediaEdge> SortProductionRoles(IEnumerable<StaffMediaEdge> source, string sort) =>
        sort switch
        {
            "SCORE_DESC"      => source.OrderByDescending(e => e.Node?.AverageScore ?? 0).ToList(),
            "FAVOURITES_DESC" => source.OrderByDescending(e => e.Node?.Favourites ?? 0).ToList(),
            "START_DATE_DESC" => source.OrderByDescending(e => e.Node?.StartDate?.Year ?? 0).ToList(),
            "START_DATE"      => source.OrderBy(e => e.Node?.StartDate?.Year ?? int.MaxValue).ToList(),
            "TITLE_ROMAJI"    => source.OrderBy(e => e.Node?.Title?.Romaji ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),
            _                 => source.OrderByDescending(e => e.Node?.Popularity ?? 0).ToList(),
        };

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
    private void SelectVoiceRolesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == _voiceRolesSort || Staff is null) return;

        ApplyVoiceRolesSortSelection(code);
        ResetDisplayedVoiceRoles();
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
    private void SelectProductionRolesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == _productionRolesSort || Staff is null) return;

        ApplyProductionRolesSortSelection(code);
        // Re-stamp badges so each card shows the value matching the new sort.
        StampProductionBadges(Staff);
        ResetDisplayedProductionRoles();
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
    private void LoadMoreVoiceRoles()
    {
        if (!VoiceRolesHasMore) return;

        var start = DisplayedVoiceRoles.Count;
        var end = Math.Min(start + LoadMorePageSize, _sortedVoiceRoles.Count);
        for (var i = start; i < end; i++)
        {
            DisplayedVoiceRoles.Add(_sortedVoiceRoles[i]);
        }
        OnPropertyChanged(nameof(VoiceRolesHasMore));
    }

    [RelayCommand]
    private void LoadMoreProductionRoles()
    {
        if (!ProductionRolesHasMore) return;

        var start = DisplayedProductionRoles.Count;
        var end = Math.Min(start + LoadMorePageSize, _sortedProductionRoles.Count);
        for (var i = start; i < end; i++)
        {
            DisplayedProductionRoles.Add(_sortedProductionRoles[i]);
        }
        OnPropertyChanged(nameof(ProductionRolesHasMore));
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
