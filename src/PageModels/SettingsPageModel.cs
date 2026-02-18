using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

public partial class SettingsPageModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAniListClient _aniListClient;
    private readonly ILogger<SettingsPageModel> _logger;

    // Snapshot of the loaded state for dirty-tracking
    private AniListUser? _loadedUser;
    private UserTitleLanguage _loadedTitleLanguage;
    private UserStaffNameLanguage _loadedStaffNameLanguage;
    private ScoreFormat _loadedScoreFormat;
    private bool _loadedDisplayAdultContent;
    private bool _loadedAiringNotifications;
    private bool _loadedRestrictMessages;
    private int _loadedActivityMergeTime;

    // --- Auth state ---
    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private string _aniListUserId = string.Empty;

    [ObservableProperty]
    private bool _isLoading = true;

    // Gate the login prompt behind IsLoading so we never flash login UI
    // while auth state is still being determined.
    public bool ShowLoginPrompt => !IsAuthenticated && !IsLoading;

    /// <summary>
    /// True when the singleton ViewModel already has authenticated profile data
    /// available for immediate display (e.g. after a flyout page switch).
    /// </summary>
    public bool HasLoadedData => _loadedUser is not null;

    [ObservableProperty]
    private bool _isSaving;

    // --- Profile hero ---
    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _userAbout = string.Empty;

    [ObservableProperty]
    private string _avatarUrl = string.Empty;

    [ObservableProperty]
    private string _bannerUrl = string.Empty;

    [ObservableProperty]
    private string _siteUrl = string.Empty;

    [ObservableProperty]
    private bool _hasBanner;

    [ObservableProperty]
    private bool _hasAbout;

    // --- Statistics ---
    [ObservableProperty]
    private string _totalAnime = "0";

    [ObservableProperty]
    private string _episodesWatched = "0";

    [ObservableProperty]
    private string _daysWatched = "0";

    [ObservableProperty]
    private string _meanScore = "0";

    // --- Display preferences ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private UserTitleLanguage _selectedTitleLanguage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private UserStaffNameLanguage _selectedStaffNameLanguage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private ScoreFormat _selectedScoreFormat;

    // --- Content & Privacy ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _displayAdultContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _airingNotifications;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _restrictMessagesToFollowing;

    // --- Activity merge time ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _activityMergeTime;

    // --- Notification preferences ---
    public ObservableCollection<NotificationToggleItem> NotificationItems { get; } = [];

    // --- App info ---
    public string AppVersion { get; } = $"v{AppInfo.Current.VersionString}";

    public bool HasUnsavedChanges =>
        _loadedUser is not null && (
            SelectedTitleLanguage != _loadedTitleLanguage ||
            SelectedStaffNameLanguage != _loadedStaffNameLanguage ||
            SelectedScoreFormat != _loadedScoreFormat ||
            DisplayAdultContent != _loadedDisplayAdultContent ||
            AiringNotifications != _loadedAiringNotifications ||
            RestrictMessagesToFollowing != _loadedRestrictMessages ||
            ActivityMergeTime != _loadedActivityMergeTime ||
            HasNotificationChanges());

    public SettingsPageModel(IAuthService authService, IAniListClient aniListClient, ILogger<SettingsPageModel> logger)
    {
        _authService = authService;
        _aniListClient = aniListClient;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        // Only show the spinner for the initial load (no cached data).
        // On refresh-with-cached-data the content view is already visible;
        // flipping IsLoading would overlay the spinner on top of it.
        var isRefresh = _loadedUser is not null;
        if (!isRefresh)
        {
            IsLoading = true;
        }

        try
        {
            await RefreshAuthStateAsync();
            StatusMessage = IsAuthenticated ? "Signed in to AniList." : "Not signed in.";

            if (IsAuthenticated)
            {
                var user = await _aniListClient.GetViewerAsync();
                _loadedUser = user;
                PopulateFromUser(user);
            }
            else
            {
                ClearUserData();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch AniList viewer data");
            StatusMessage = "Failed to load profile.";
        }
        finally
        {
            IsLoading = false;
        }

        SentrySdk.AddBreadcrumb("Settings loaded", "navigation", "state");
    }

    private void PopulateFromUser(AniListUser user)
    {
        AniListUserId = user.Id.ToString();
        UserName = user.Name;
        UserAbout = user.About ?? string.Empty;
        HasAbout = !string.IsNullOrWhiteSpace(user.About);
        AvatarUrl = user.AvatarLarge ?? user.AvatarMedium ?? string.Empty;
        BannerUrl = user.BannerImage ?? string.Empty;
        HasBanner = !string.IsNullOrWhiteSpace(user.BannerImage);
        SiteUrl = user.SiteUrl ?? string.Empty;

        // Statistics
        TotalAnime = user.AnimeStatistics.Count.ToString("N0");
        EpisodesWatched = user.AnimeStatistics.EpisodesWatched.ToString("N0");
        var days = user.AnimeStatistics.MinutesWatched / 1440.0;
        DaysWatched = days.ToString("N1");
        MeanScore = user.AnimeStatistics.MeanScore.ToString("N1");

        // Display preferences
        SelectedTitleLanguage = user.Options.TitleLanguage;
        SelectedStaffNameLanguage = user.Options.StaffNameLanguage;
        SelectedScoreFormat = user.ScoreFormat;

        // Content & Privacy
        DisplayAdultContent = user.Options.DisplayAdultContent;
        AiringNotifications = user.Options.AiringNotifications;
        RestrictMessagesToFollowing = user.Options.RestrictMessagesToFollowing;

        // Activity merge time
        ActivityMergeTime = user.Options.ActivityMergeTime;

        // Notifications
        PopulateNotificationItems(user.Options.NotificationOptions);

        // Snapshot for dirty-tracking
        _loadedTitleLanguage = SelectedTitleLanguage;
        _loadedStaffNameLanguage = SelectedStaffNameLanguage;
        _loadedScoreFormat = SelectedScoreFormat;
        _loadedDisplayAdultContent = DisplayAdultContent;
        _loadedAiringNotifications = AiringNotifications;
        _loadedRestrictMessages = RestrictMessagesToFollowing;
        _loadedActivityMergeTime = ActivityMergeTime;

        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void PopulateNotificationItems(List<NotificationOption> options)
    {
        NotificationItems.Clear();

        var allTypes = new (string Type, string DisplayName, string Category)[]
        {
            ("ACTIVITY_MESSAGE", "Messages", "Activity"),
            ("ACTIVITY_REPLY", "Replies", "Activity"),
            ("ACTIVITY_MENTION", "Mentions", "Activity"),
            ("ACTIVITY_LIKE", "Likes", "Activity"),
            ("ACTIVITY_REPLY_LIKE", "Reply Likes", "Activity"),
            ("ACTIVITY_REPLY_SUBSCRIBED", "Reply Subscribed", "Activity"),
            ("THREAD_COMMENT_MENTION", "Comment Mentions", "Forum"),
            ("THREAD_SUBSCRIBED", "Subscribed Threads", "Forum"),
            ("THREAD_COMMENT_REPLY", "Comment Replies", "Forum"),
            ("THREAD_LIKE", "Thread Likes", "Forum"),
            ("THREAD_COMMENT_LIKE", "Comment Likes", "Forum"),
            ("AIRING", "Airing", "Media"),
            ("RELATED_MEDIA_ADDITION", "Related Media", "Media"),
            ("MEDIA_DATA_CHANGE", "Data Changes", "Media"),
            ("MEDIA_MERGE", "Merges", "Media"),
            ("MEDIA_DELETION", "Deletions", "Media"),
            ("FOLLOWING", "New Followers", "Social"),
        };

        var lookup = options.ToDictionary(o => o.Type, o => o.Enabled, StringComparer.OrdinalIgnoreCase);

        foreach (var (type, displayName, category) in allTypes)
        {
            var enabled = lookup.TryGetValue(type, out var val) && val;
            var item = new NotificationToggleItem(type, displayName, category, enabled);
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HasUnsavedChanges));
            NotificationItems.Add(item);
        }
    }

    private void ClearUserData()
    {
        _loadedUser = null;
        AniListUserId = string.Empty;
        UserName = string.Empty;
        UserAbout = string.Empty;
        HasAbout = false;
        AvatarUrl = string.Empty;
        BannerUrl = string.Empty;
        HasBanner = false;
        SiteUrl = string.Empty;
        TotalAnime = "0";
        EpisodesWatched = "0";
        DaysWatched = "0";
        MeanScore = "0";
        NotificationItems.Clear();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private bool HasNotificationChanges()
    {
        if (_loadedUser is null)
        {
            return false;
        }

        var loaded = _loadedUser.Options.NotificationOptions
            .ToDictionary(o => o.Type, o => o.Enabled, StringComparer.OrdinalIgnoreCase);
        foreach (var item in NotificationItems)
        {
            var wasEnabled = loaded.TryGetValue(item.Type, out var val) && val;
            if (item.IsEnabled != wasEnabled)
            {
                return true;
            }
        }
        return false;
    }

    partial void OnStatusMessageChanged(string value)
        => HasStatusMessage = !string.IsNullOrWhiteSpace(value);

    partial void OnIsLoadingChanged(bool value)
        => OnPropertyChanged(nameof(ShowLoginPrompt));

    partial void OnIsAuthenticatedChanged(bool value)
        => OnPropertyChanged(nameof(ShowLoginPrompt));

    [RelayCommand]
    private async Task SignIn()
    {
        _logger.LogInformation("Sign-in requested from Settings.");
        try
        {
            SentrySdk.AddBreadcrumb("Sign-in requested (Settings)", "auth", "user");
            var signedIn = await _authService.SignInAsync();
            await RefreshAuthStateAsync();

            if (signedIn)
            {
                StatusMessage = "Signed in to AniList.";
                await LoadAsync();
            }
            else
            {
                StatusMessage = "Sign in canceled.";
            }

            SentrySdk.AddBreadcrumb(
                signedIn ? "Sign-in successful (Settings)" : "Sign-in canceled (Settings)",
                "auth",
                "user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sign-in failed.");
            SentrySdk.AddBreadcrumb("Sign-in failed (Settings)", "auth", "user");
            StatusMessage = "Sign in failed. Try again.";
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        _logger.LogInformation("Sign-out requested from Settings.");
        SentrySdk.AddBreadcrumb("Sign-out requested (Settings)", "auth", "user");
        await _authService.SignOutAsync();
        await RefreshAuthStateAsync();
        ClearUserData();
        StatusMessage = "Signed out.";
    }

    [RelayCommand]
    private void SetTitleLanguage(string value)
    {
        if (Enum.TryParse<UserTitleLanguage>(value, out var lang))
        {
            SelectedTitleLanguage = lang;
        }
    }

    [RelayCommand]
    private void SetStaffNameLanguage(string value)
    {
        if (Enum.TryParse<UserStaffNameLanguage>(value, out var lang))
        {
            SelectedStaffNameLanguage = lang;
        }
    }

    [RelayCommand]
    private void SetScoreFormat(string value)
    {
        if (Enum.TryParse<ScoreFormat>(value, out var format))
        {
            SelectedScoreFormat = format;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (!HasUnsavedChanges || IsSaving)
        {
            return;
        }

        IsSaving = true;
        StatusMessage = "Saving...";
        try
        {
            var request = new UpdateUserRequest
            {
                TitleLanguage = SelectedTitleLanguage,
                DisplayAdultContent = DisplayAdultContent,
                AiringNotifications = AiringNotifications,
                ScoreFormat = SelectedScoreFormat,
                StaffNameLanguage = SelectedStaffNameLanguage,
                RestrictMessagesToFollowing = RestrictMessagesToFollowing,
                ActivityMergeTime = ActivityMergeTime,
                NotificationOptions = NotificationItems
                    .Select(n => new NotificationOptionInput { Type = n.Type, Enabled = n.IsEnabled })
                    .ToList()
            };

            var updatedUser = await _aniListClient.UpdateUserAsync(request);
            _loadedUser = updatedUser;
            PopulateFromUser(updatedUser);
            StatusMessage = "Settings saved!";
            SentrySdk.AddBreadcrumb("Settings saved", "settings", "user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            StatusMessage = "Failed to save settings. Try again.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task OpenAniListProfile()
    {
        if (string.IsNullOrWhiteSpace(SiteUrl))
        {
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(new Uri(SiteUrl), BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open AniList profile URL");
        }
    }

    // TODO: This might be dead code
    [RelayCommand]
    private async Task OpenAniListSettings()
    {
        try
        {
            await Browser.Default.OpenAsync(new Uri("https://anilist.co/settings"), BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open AniList settings URL");
        }
    }

    private async Task RefreshAuthStateAsync()
    {
        var token = await _authService.GetAccessTokenAsync();
        IsAuthenticated = !string.IsNullOrWhiteSpace(token);
    }
}

public partial class NotificationToggleItem : ObservableObject
{
    public string Type { get; }
    public string DisplayName { get; }
    public string Category { get; }

    [ObservableProperty]
    private bool _isEnabled;

    public NotificationToggleItem(string type, string displayName, string category, bool isEnabled)
    {
        Type = type;
        DisplayName = displayName;
        Category = category;
        _isEnabled = isEnabled;
    }
}
