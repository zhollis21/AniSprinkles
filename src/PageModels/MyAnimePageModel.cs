using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

public partial class MyAnimePageModel : ObservableObject
{
    private static readonly TimeSpan ListRefreshInterval = TimeSpan.FromMinutes(5);
    private const string DetailsRoute = "media-details";

    private readonly IAniListClient _aniListClient;
    private readonly IAuthService _authService;
    private readonly ErrorReportService _errorReportService;
    private readonly ILogger<MyAnimePageModel> _logger;
    private bool _hasLoaded;
    private DateTimeOffset _lastSuccessfulLoadUtc;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoginPrompt))]
    private bool _isAuthenticationPending = true;

    public bool ShowLoginPrompt => !IsAuthenticated && !IsAuthenticationPending;

    // Set by the page code-behind during the fast-path deferred rebuild to
    // keep a spinner visible while a new content-view instance is being created.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInitialLoading))]
    private bool _isRebuilding;

    // Keep list loading UX aligned with details page: a single centered first-load indicator.
    // Also true while IsRebuilding so the fast-path return trip shows a spinner
    // instead of a blank page during the Shell-transition deferred delay.
    public bool IsInitialLoading => (IsBusy && IsAuthenticated && Sections.Count == 0) || IsRebuilding;

    /// <summary>
    /// True when the singleton ViewModel already has data that can be shown
    /// immediately (e.g. when a new page instance is created on back-navigation
    /// but the ViewModel's cached sections are still valid).
    /// </summary>
    public bool HasLoadedData => _hasLoaded && Sections.Count > 0;

    [ObservableProperty]
    private bool _isNavigatingToDetails;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _title = "My Anime";

    [ObservableProperty]
    private ObservableCollection<MediaListSection> _sections = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    [ObservableProperty]
    private bool _hasErrorDetails;

    [ObservableProperty]
    private bool _isErrorDetailsVisible;

    // ── Sort / Filter state ───────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortFieldDisplay))]
    [NotifyPropertyChangedFor(nameof(SortDirectionIcon))]
    private SortField _currentSortField = SortField.LastUpdated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionIcon))]
    private bool _sortAscending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isFilterBarVisible;

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    public string SortFieldDisplay => CurrentSortField switch
    {
        SortField.Title => "Title",
        SortField.Score => "Score",
        SortField.Progress => "Progress",
        _ => "Updated",
    };

    public string SortDirectionIcon => SortAscending ? "↑" : "↓";

    public MyAnimePageModel(IAniListClient aniListClient, IAuthService authService, ErrorReportService errorReportService, ILogger<MyAnimePageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _errorReportService = errorReportService;
        _logger = logger;
    }

    public async Task LoadAsync(bool forceReload = false)
    {
        if (IsBusy)
        {
            return;
        }

        var token = await _authService.GetAccessTokenAsync();
        var isAuthenticated = !string.IsNullOrWhiteSpace(token);
        // OnAppearing can fire often; keep list navigation snappy by skipping refreshes inside a short stale window.
        var isFresh = _lastSuccessfulLoadUtc != default &&
            DateTimeOffset.UtcNow - _lastSuccessfulLoadUtc < ListRefreshInterval;

        if (_hasLoaded && !forceReload && isAuthenticated == IsAuthenticated)
        {
            if (!isAuthenticated || isFresh)
            {
                return;
            }
        }

        if (forceReload)
        {
            _lastSuccessfulLoadUtc = default;
        }

        var hadExistingSections = Sections.Count > 0;
        // Keep user context (expanded/collapsed groups) when data refreshes.
        var expandedStates = Sections.ToDictionary(
            section => section.Title,
            section => section.IsExpanded,
            StringComparer.Ordinal);

        IsBusy = true;
        try
        {
            _logger.LogInformation("Loading My Anime list.");
            SentrySdk.AddBreadcrumb("Load My Anime list", "navigation", "state");

            IsAuthenticated = isAuthenticated;
            IsAuthenticationPending = false;

            if (!IsAuthenticated)
            {
                Title = "Sign in required";
                StatusMessage = "Sign in to see your AniList.";
                ErrorDetails = string.Empty;
                IsErrorDetailsVisible = false;
                Sections = [];
                _hasLoaded = true;
                _lastSuccessfulLoadUtc = default;
                return;
            }

            Title = "My Anime";
            StatusMessage = string.Empty;
            ErrorDetails = string.Empty;
            IsErrorDetailsVisible = false;

            // On first authenticated load (fresh install or after sign-in on another device),
            // sync display preferences from AniList before fetching the list.
            if (!AppSettings.HasSynced)
            {
                try
                {
                    var viewer = await _aniListClient.GetViewerAsync();
                    AppSettings.TitleLanguage = viewer.Options.TitleLanguage;
                    AppSettings.ScoreFormat = viewer.ScoreFormat;
                    AppSettings.DisplayAdultContent = viewer.Options.DisplayAdultContent;
                    AppSettings.Save();
                }
                catch (Exception viewerEx)
                {
                    _logger.LogWarning(viewerEx, "Failed to sync viewer preferences on first load");
                }
            }

            SentrySdk.AddBreadcrumb("Fetching AniList list", "http", "state");
            var list = await _aniListClient.GetMyAnimeListAsync();
            // Grouping can be heavy on large lists; build sections off the UI thread.
            var sortField = CurrentSortField;
            var sortAscending = SortAscending;
            var filterText = SearchText;
            var sections = await Task.Run(() => BuildSections(list, expandedStates, sortField, sortAscending, filterText));
            Sections = sections;
            _hasLoaded = true;
            _lastSuccessfulLoadUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            if (hadExistingSections && IsAuthenticated)
            {
                // Prefer stale data over blank UI when refresh fails after a previously successful load.
                StatusMessage = "Refresh failed. Showing cached list.";
                _hasLoaded = true;
            }
            else
            {
                StatusMessage = "Failed to load list. Tap Details for more.";
                Sections = [];
                _hasLoaded = false;
            }

            ErrorDetails = _errorReportService.Record(ex, "Load My Anime");
            IsErrorDetailsVisible = false;
        }
        finally
        {
            IsBusy = false;
            IsAuthenticationPending = false;
        }
    }

    partial void OnStatusMessageChanged(string value)
        => HasStatusMessage = !string.IsNullOrWhiteSpace(value);

    partial void OnErrorDetailsChanged(string value)
        => HasErrorDetails = !string.IsNullOrWhiteSpace(value);

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(IsInitialLoading));

    partial void OnIsAuthenticatedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInitialLoading));
        OnPropertyChanged(nameof(ShowLoginPrompt));
    }

    partial void OnSectionsChanged(ObservableCollection<MediaListSection> value)
        => OnPropertyChanged(nameof(IsInitialLoading));


    [RelayCommand]
    private async Task SignIn()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _logger.LogInformation("Sign-in requested.");
            SentrySdk.AddBreadcrumb("Sign-in requested", "auth", "user");
            var signedIn = await _authService.SignInAsync();
            if (!signedIn)
            {
                StatusMessage = "Sign in canceled.";
                SentrySdk.AddBreadcrumb("Sign-in canceled", "auth", "user");
                return;
            }

            SentrySdk.AddBreadcrumb("Sign-in successful", "auth", "user");
        }
        catch (Exception ex)
        {
            StatusMessage = "Sign in failed. Tap Details for more.";
            ErrorDetails = _errorReportService.Record(ex, "Sign in");
            IsErrorDetailsVisible = false;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(forceReload: true);
    }

    [RelayCommand]
    private async Task SignOut()
    {
        _logger.LogInformation("Sign-out requested.");
        SentrySdk.AddBreadcrumb("Sign-out requested", "auth", "user");
        await _authService.SignOutAsync();
        AppSettings.Clear();
        await LoadAsync(forceReload: true);
    }

    [RelayCommand]
    private async Task OpenDetails(MediaListEntry? entry)
    {
        if (entry is null || IsNavigatingToDetails)
        {
            return;
        }

        var mediaId = entry.MediaId != 0 ? entry.MediaId : entry.Media?.Id ?? 0;
        if (mediaId <= 0)
        {
            StatusMessage = "Unable to open details.";
            return;
        }

        // SelectionChanged can fire again before navigation completes on fast repeat taps.
        // Gate route pushes so we do not stack multiple details pages for the same user action.
        IsNavigatingToDetails = true;
        try
        {
            var navTraceId = $"{mediaId}-{Environment.TickCount64}";
            var navStartUtc = DateTimeOffset.UtcNow;
            var navStopwatch = Stopwatch.StartNew();
            _logger.LogInformation(
                "NAVTRACE {TraceId} tap accepted for media {MediaId} at {StartedUtc:O}",
                navTraceId,
                mediaId,
                navStartUtc);
            SentrySdk.AddBreadcrumb($"Open details {mediaId}", "navigation", "state");
            // Keep route payload minimal so navigation is not blocked by passing a full list-entry graph.
            // Use non-animated transition: the details page shows its own loading shell immediately,
            // and disabling the slide transition allows destination page to render without animation overhead.
            await Shell.Current.GoToAsync(DetailsRoute, animate: false, new Dictionary<string, object>
            {
                ["mediaId"] = mediaId,
                ["navTraceId"] = navTraceId,
                ["navStartUtcTicks"] = navStartUtc.UtcTicks
            });
            navStopwatch.Stop();
            _logger.LogInformation(
                "NAVTRACE {TraceId} GoToAsync completed in {ElapsedMs}ms",
                navTraceId,
                navStopwatch.ElapsedMilliseconds);
        }
        finally
        {
            IsNavigatingToDetails = false;
        }
    }

    private static ObservableCollection<MediaListSection> BuildSections(
        IReadOnlyList<MediaListEntry> entries,
        IReadOnlyDictionary<string, bool> expandedStates,
        SortField sortField,
        bool sortAscending,
        string filterText)
    {
        var sections = new List<MediaListSection>
        {
            CreateSection("Watching", defaultExpanded: true, expandedStates),
            CreateSection("Planning", defaultExpanded: false, expandedStates),
            CreateSection("Completed", defaultExpanded: false, expandedStates),
            CreateSection("Paused", defaultExpanded: false, expandedStates),
            CreateSection("Dropped", defaultExpanded: false, expandedStates),
            CreateSection("Repeating", defaultExpanded: false, expandedStates)
        };

        var map = new Dictionary<MediaListStatus, MediaListSection>
        {
            [MediaListStatus.Current] = sections[0],
            [MediaListStatus.Planning] = sections[1],
            [MediaListStatus.Completed] = sections[2],
            [MediaListStatus.Paused] = sections[3],
            [MediaListStatus.Dropped] = sections[4],
            [MediaListStatus.Repeating] = sections[5]
        };

        var unknown = CreateSection("Other", defaultExpanded: false, expandedStates);
        var buckets = sections.ToDictionary(section => section, _ => new List<MediaListEntry>());
        var unknownBucket = new List<MediaListEntry>();

        var hideAdult = !AppSettings.DisplayAdultContent;

        foreach (var entry in entries)
        {
            if (hideAdult && entry.Media?.IsAdult == true)
            {
                continue;
            }

            if (entry.Status is null || !map.TryGetValue(entry.Status.Value, out var section))
            {
                unknownBucket.Add(entry);
                continue;
            }

            buckets[section].Add(entry);
        }

        foreach (var section in sections)
        {
            section.AddItems(buckets[section]);
        }

        if (unknownBucket.Count > 0)
        {
            unknown.AddItems(unknownBucket);
        }

        // Apply current sort and filter to every section.
        foreach (var section in sections)
        {
            section.ApplySort(sortField, sortAscending);
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                section.ApplyFilter(filterText);
            }
        }

        if (unknown.TotalCount > 0)
        {
            unknown.ApplySort(sortField, sortAscending);
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                unknown.ApplyFilter(filterText);
            }
        }

        var result = new ObservableCollection<MediaListSection>(
            sections.Where(s => s.TotalCount > 0));

        if (unknown.TotalCount > 0)
        {
            result.Add(unknown);
        }

        return result;
    }

    private static MediaListSection CreateSection(
        string title,
        bool defaultExpanded,
        IReadOnlyDictionary<string, bool> expandedStates)
    {
        if (expandedStates.TryGetValue(title, out var expanded))
        {
            return new MediaListSection(title, expanded);
        }

        return new MediaListSection(title, defaultExpanded);
    }

    // ── Sort / Filter commands ──────────────────────────────────────

    [RelayCommand]
    private void ToggleFilterBar()
    {
        IsFilterBarVisible = !IsFilterBarVisible;

        if (!IsFilterBarVisible && HasSearchText)
        {
            SearchText = string.Empty;
            ApplyFilterToSections();
        }
    }

    [RelayCommand]
    private void SetSortField(string field)
    {
        if (!Enum.TryParse<SortField>(field, out var parsed))
        {
            return;
        }

        if (CurrentSortField == parsed)
        {
            // Same field tapped → toggle direction.
            SortAscending = !SortAscending;
        }
        else
        {
            CurrentSortField = parsed;
            // Default direction per field type.
            SortAscending = parsed == SortField.Title;
        }

        ApplySortToSections();
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortAscending = !SortAscending;
        ApplySortToSections();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        ApplyFilterToSections();
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        ApplyFilterToSections();
    }

    private void ApplySortToSections()
    {
        foreach (var section in Sections)
        {
            section.ApplySort(CurrentSortField, SortAscending);
        }
    }

    private void ApplyFilterToSections()
    {
        foreach (var section in Sections)
        {
            section.ApplyFilter(SearchText);
        }
    }

    // ── Details / Error commands ─────────────────────────────────────

    [RelayCommand]
    private void ToggleDetails()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        IsErrorDetailsVisible = !IsErrorDetailsVisible;
    }

    [RelayCommand]
    private async Task CopyError()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(ErrorDetails);
        StatusMessage = "Error details copied.";
    }

    [RelayCommand]
    private async Task ShareError()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = ErrorDetails,
            Title = "AniSprinkles Error Details"
        });
    }
}
