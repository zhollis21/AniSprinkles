using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using AniSprinkles.Utilities;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
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

    private const int PageSize = 25;
    private const string VoiceRolesDefaultSort = "FAVOURITES_DESC";
    private const string ProductionRolesDefaultSort = "POPULARITY_DESC";

    private int _loadedStaffId;
    private ParsedDescription _parsedDescription = ParsedDescription.Empty;
    private readonly PageLoadScope _scope = new();

    // Voice Roles (Staff.characters) and Production Roles (Staff.staffMedia) are two genuinely
    // separate AniList connections — each lazily paged and server-side sorted, fully independent.
    private readonly PaginatedSection<StaffCharacterEdge> _voiceRoles;
    private readonly PaginatedSection<StaffMediaEdge> _productionRoles;

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
        INavigationService navigationService,
        ILogger<StaffDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _navigationService = navigationService;
        _logger = logger;

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

    public bool HasVoiceRoles => _voiceRoles.Items.Count > 0;
    public bool HasProductionRoles => _productionRoles.Items.Count > 0;

    public bool VoiceRolesBusy => _voiceRoles.IsBusy;
    public bool ProductionRolesBusy => _productionRoles.IsBusy;

    public string VoiceRolesSort => _voiceRoles.Sort;
    public string ProductionRolesSort => _productionRoles.Sort;

    // ---- Hero / bio / quick facts (unchanged) ---------------------------------------------------

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

    public bool HasSiteUrl => !string.IsNullOrWhiteSpace(Staff?.SiteUrl);

    partial void OnStaffChanged(Staff? value)
    {
        _parsedDescription = DescriptionParser.Parse(value?.Description);
    }

    partial void OnCurrentStateChanged(PageState oldValue, PageState newValue)
        => _logger.LogInformation("PageState: {OldState} → {NewState} (key={StateKey})", oldValue, newValue, CurrentStateKey ?? "(null)");

    // ---- Load -----------------------------------------------------------------------------------

    public async Task LoadAsync(int staffId)
    {
        if (staffId <= 0)
        {
            ShowError("Not Found", "Invalid staff id.", canRetry: false);
            return;
        }

        // Same staff already loaded: keep its sections + sort and just restore Content state. This is hit
        // when returning from a pushed sub-page and — importantly — when a CommunityToolkit sort popup
        // closes (it fires the host page's OnAppearing → reload). Without this guard the popup would reset
        // the sort the user just picked. Mirrors MediaDetailsPageModel.
        if (Staff is not null && Staff.Id == staffId)
        {
            CurrentState = PageState.Content;
            return;
        }

        _loadedStaffId = staffId;
        var token = _scope.Begin(); // fresh page scope; OnDisappearing cancels it on navigate-away



        IsBusy = true;
        if (Staff is null || Staff.Id != staffId)
        {
            CurrentState = PageState.InitialLoading;
            IsShowingSpoilers = false;
            IsDescriptionExpanded = false;
        }

        _voiceRoles.Reset();
        _productionRoles.Reset();
        ResetVoiceRolesSortSelection();
        ResetProductionRolesSortSelection();

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("NAVTRACE StaffDetails load start (staff {StaffId})", staffId);

        try
        {
            var staff = await _aniListClient.GetStaffAsync(staffId, cancellationToken: token).ConfigureAwait(true);
            if (staff is null)
            {
                _logger.LogInformation("NAVTRACE StaffDetails not found in {ElapsedMs}ms (staff {StaffId})", stopwatch.ElapsedMilliseconds, staffId);
                ShowError("Not Found", "We couldn't find this staff member.", canRetry: false);
                return;
            }

            Staff = staff;

            _voiceRoles.Seed(staff.Characters.ToList(), staff.CharactersPageInfo);
            _productionRoles.Seed(staff.StaffMedia.ToList(), staff.StaffMediaPageInfo);

            CurrentState = PageState.Content;
            _logger.LogInformation(
                "NAVTRACE StaffDetails fetch+seed in {ElapsedMs}ms (staff {StaffId}, {VoiceRoles} voice roles, {ProductionRoles} production roles); UI render follows",
                stopwatch.ElapsedMilliseconds, staffId, _voiceRoles.Items.Count, _productionRoles.Items.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("NAVTRACE StaffDetails load cancelled after {ElapsedMs}ms (staff {StaffId})", stopwatch.ElapsedMilliseconds, staffId);
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;
            var isNotFound = apiEx?.Kind == ApiErrorKind.NotFound;
            if (isNotFound)
            {
                // NotFound is non-retryable and intentionally kept out of Sentry — log at Warning so it stays a breadcrumb.
                _logger.LogWarning(ex, "NAVTRACE StaffDetails not found on AniList in {ElapsedMs}ms (staff {StaffId})", stopwatch.ElapsedMilliseconds, staffId);
            }
            else
            {
                _logger.LogError(ex, "NAVTRACE StaffDetails load failed in {ElapsedMs}ms (staff {StaffId})", stopwatch.ElapsedMilliseconds, staffId);
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

    private Task<(IReadOnlyList<StaffCharacterEdge> Items, PageInfo? PageInfo)> FetchVoiceRolesPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => _aniListClient.LoadStaffCharactersPageAsync(_loadedStaffId, page, sort, PageSize, cancellationToken);

    private Task<(IReadOnlyList<StaffMediaEdge> Items, PageInfo? PageInfo)> FetchProductionRolesPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => _aniListClient.LoadStaffMediaPageAsync(_loadedStaffId, page, sort, PageSize, cancellationToken);

    // ---- Commands -------------------------------------------------------------------------------

    // CanExecute gates the scroll-threshold trigger: with no next page or while a fetch/sort is in
    // flight, the CollectionView's RemainingItemsThresholdReached can't re-invoke this (which would
    // otherwise log a no-op LISTTRACE pair on every scroll-to-end). LoadMoreAsync stays guarded too.
    [RelayCommand(CanExecute = nameof(CanLoadMoreVoiceRoles))]
    private Task LoadMoreVoiceRoles()
        => RunTracedListOpAsync(
            "Voice Roles · Load More",
            () => _voiceRoles.LoadMoreAsync(_scope.EnsureActive()),
            () => _voiceRoles.Items.Count);

    private bool CanLoadMoreVoiceRoles() => _voiceRoles.CanLoadMore;

    [RelayCommand(CanExecute = nameof(CanLoadMoreProductionRoles))]
    private Task LoadMoreProductionRoles()
        => RunTracedListOpAsync(
            "Production Roles · Load More",
            () => _productionRoles.LoadMoreAsync(_scope.EnsureActive()),
            () => _productionRoles.Items.Count);

    private bool CanLoadMoreProductionRoles() => _productionRoles.CanLoadMore;

    [RelayCommand]
    private Task SelectVoiceRolesSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, _voiceRoles.Sort, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return RunTracedListOpAsync(
            $"Voice Roles · sort→{code}",
            () => _voiceRoles.ChangeSortAsync(code, _scope.EnsureActive()),
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

        return RunTracedListOpAsync(
            $"Production Roles · sort→{code}",
            () => _productionRoles.ChangeSortAsync(code, _scope.EnsureActive()),
            () => _productionRoles.Items.Count,
            onComplete: () => SyncSortSelection(ProductionRolesSortOptions, _productionRoles.Sort));
    }

    // LISTTRACE: times the network fetch + collection apply so API cost (logged here) is separable
    // from the UI render of the bound list (which happens after this returns, on the UI thread).
    private async Task RunTracedListOpAsync(string op, Func<Task> operation, Func<int> loadedCount, Action? onComplete = null)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("LISTTRACE {Op} start (staff {StaffId})", op, _loadedStaffId);

        Exception? failure = null;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        // Stop + log the timed section BEFORE any user feedback, so the snackbar's display time never
        // inflates the reported fetch+apply duration (failures are exactly what we want to time).
        stopwatch.Stop();
        onComplete?.Invoke();

        if (failure is null)
        {
            _logger.LogInformation(
                "LISTTRACE {Op} completed in {ElapsedMs}ms ({Count} loaded); UI render follows",
                op, stopwatch.ElapsedMilliseconds, loadedCount());
            return;
        }

        _logger.LogWarning(failure, "LISTTRACE {Op} failed in {ElapsedMs}ms (staff {StaffId})", op, stopwatch.ElapsedMilliseconds, _loadedStaffId);
        await ShowListErrorSnackbarAsync(failure).ConfigureAwait(true);
    }

    // A failed sort/Load More leaves the existing list intact; surface a transient message so the
    // failure isn't silent. Use the subtitle (the actionable guidance) rather than the terse title,
    // so the toast reads as clearly as the full-page error state does.
    private Task ShowListErrorSnackbarAsync(Exception ex)
    {
        var message = ex is AniListApiException apiEx
            ? apiEx.UserSubtitle
            : "Couldn't update the list. Check your connection and try again.";
        return ShowSnackbarAsync(message);
    }

    private async Task ShowSnackbarAsync(string message)
    {
        try
        {
            await Snackbar.Make(message, duration: TimeSpan.FromSeconds(4)).Show().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snackbar display failed");
        }
    }

    private async Task ShowToastAsync(string message)
    {
        try
        {
            await Toast.Make(message, ToastDuration.Short).Show().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toast display failed");
        }
    }

    [RelayCommand]
    private void ToggleSpoilers() => IsShowingSpoilers = !IsShowingSpoilers;

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

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
    private async Task NavigateToMedia(RelatedMedia? media)
    {
        var mediaId = media?.Id ?? 0;
        _logger.LogInformation("NAVTRACE Staff→Media with id={MediaId}", mediaId);
        if (mediaId <= 0)
        {
            return;
        }

        // Detail screen is anime-only (Media(id:, type: ANIME)); a manga/novel id would 404.
        // Staff production roles and voice-role media can include manga, so toast instead of navigating.
        if (media is { IsAnime: false })
        {
            _logger.LogInformation("NAVTRACE Staff→Media skipped non-anime {MediaId} (type={Type}).", mediaId, media.Type);
            await ShowToastAsync("Manga & Novel details aren't supported yet.");
            return;
        }

        await _navigationService.GoToAsync("media-details", animate: false, new Dictionary<string, object>
        {
            ["mediaId"] = mediaId,
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
            edge.MetricBadge = BuildProductionMetricBadge(edge.Node, sort);
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

    private static (string Title, string Subtitle) DescribeError(Exception ex)
        => ex is AniListApiException apiEx
            ? (apiEx.UserTitle, apiEx.UserSubtitle)
            : ("Something Went Wrong", "Failed to load staff details.");

    private static ItemMetricBadge? BuildProductionMetricBadge(RelatedMedia? media, string sort)
    {
        if (media is null)
        {
            return null;
        }

        // When the active sort IS a metric, always show the badge with a 0/— fallback so missing data doesn't
        // look broken; only non-metric sorts (Title) show no badge.
        return sort switch
        {
            "POPULARITY_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.People24,
                IconColor = Color.FromArgb("#FF9500"),
                Text = media.PopularityOrZero,
            },
            "SCORE_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Star24,
                IconColor = Color.FromArgb("#FFCC00"),
                Text = media.ScoreOrDash,
            },
            "FAVOURITES_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Heart24,
                IconColor = Color.FromArgb("#FF2D95"),
                Text = media.FavouritesOrZero,
            },
            "START_DATE_DESC" or "START_DATE" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Calendar24,
                IconColor = Color.FromArgb("#00C2FF"),
                Text = media.YearOrDash,
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

public sealed record QuickFactChip(string Display);
