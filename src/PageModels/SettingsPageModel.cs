using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

public partial class SettingsPageModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAniListClient _aniListClient;
    private readonly IAiringNotificationService _airingNotificationService;
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

    // ── Main page state (mutually exclusive) ────────────────────────
    // Transitions:
    //   InitialLoading → Content (authenticated load) | Unauthenticated (no user)
    //   Unauthenticated → InitialLoading (on sign-in)
    //   Content        → Content (refresh) | Unauthenticated (sign-out)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.InitialLoading;

    // StateContainer.CurrentState is typed as string; null/empty restores default
    // children (the loaded content host). Non-Content states match a StateView key.
    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

    // --- Auth state ---
    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private string _aniListUserId = string.Empty;

    /// <summary>
    /// True when the singleton ViewModel already has authenticated profile data
    /// available for immediate display (e.g. after a flyout page switch).
    /// </summary>
    public bool HasLoadedData => _loadedUser is not null;

    [ObservableProperty]
    private bool _isSaving;

    private CancellationTokenSource? _saveDebounceCts;

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

    public SettingsPageModel(IAuthService authService, IAniListClient aniListClient, IAiringNotificationService airingNotificationService, ILogger<SettingsPageModel> logger)
    {
        _authService = authService;
        _aniListClient = aniListClient;
        _airingNotificationService = airingNotificationService;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        // Only show the spinner for the initial load (no cached data).
        // On refresh-with-cached-data the content view is already visible;
        // flipping CurrentState would overlay the spinner on top of it.
        var isRefresh = _loadedUser is not null;
        if (!isRefresh)
        {
            CurrentState = PageState.InitialLoading;
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
                CurrentState = PageState.Content;
            }
            else
            {
                ClearUserData();
                CurrentState = PageState.Unauthenticated;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch AniList viewer data");
            StatusMessage = "Failed to load profile.";
            // Preserve cached Content when available; otherwise fall back to
            // Unauthenticated so the user can retry sign-in from the login card.
            CurrentState = _loadedUser is not null ? PageState.Content : PageState.Unauthenticated;
        }

        SentrySdk.AddBreadcrumb("Settings loaded", "navigation", "state");
    }

    private void PopulateFromUser(AniListUser user)
    {
        // Suppress the notification toggle handler while populating from server state.
        // The explicit SchedulePeriodicCheck() call at the end handles re-enabling.
        _suppressNotificationToggle = true;

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

        // Sync local app settings from user profile
        AppSettings.SyncFromViewer(user);

        _suppressNotificationToggle = false;

        // Re-enable WorkManager if the user has airing notifications enabled.
        // Check permission first — existing users may have the toggle ON from AniList
        // but haven't granted POST_NOTIFICATIONS on this device yet.
        if (AiringNotifications)
        {
            _ = EnsureNotificationPermissionAndScheduleAsync();
        }

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
            item.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasUnsavedChanges));
                TriggerAutoSave();
            };
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

    partial void OnSelectedTitleLanguageChanged(UserTitleLanguage value) => TriggerAutoSave();
    partial void OnSelectedStaffNameLanguageChanged(UserStaffNameLanguage value) => TriggerAutoSave();
    partial void OnSelectedScoreFormatChanged(ScoreFormat value) => TriggerAutoSave();
    partial void OnDisplayAdultContentChanged(bool value) => TriggerAutoSave();
    partial void OnAiringNotificationsChanged(bool value)
    {
        // Do not queue an auto-save here — the permission dialog may take >1500ms to answer,
        // causing the debounce to fire with the wrong value before the result is known.
        // HandleAiringNotificationToggleAsync cancels any pending save on entry and queues
        // a fresh one after the permission flow resolves with the final value.
        // The suppress flag guards the revert path so the internal toggle reset doesn't queue a save.
        if (!_suppressNotificationToggle)
        {
            _ = HandleAiringNotificationToggleAsync(value);
        }
    }
    partial void OnRestrictMessagesToFollowingChanged(bool value) => TriggerAutoSave();
    partial void OnActivityMergeTimeChanged(int value) => TriggerAutoSave();

    private void TriggerAutoSave()
    {
        if (_loadedUser is null || !HasUnsavedChanges)
        {
            return;
        }

        _ = DebouncedSaveAsync();
    }

    private async Task DebouncedSaveAsync()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts = new CancellationTokenSource();
        var token = _saveDebounceCts.Token;

        try
        {
            await Task.Delay(1500, token);
            if (HasUnsavedChanges)
            {
                await SaveSettingsAsync();
            }
        }
        catch (TaskCanceledException) { }
    }

    private async Task SaveSettingsAsync()
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
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
            SentrySdk.AddBreadcrumb("Settings auto-saved", "settings", "user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-save settings");
            StatusMessage = "Failed to save settings.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool _suppressNotificationToggle;

    // Prevents concurrent executions of HandleAiringNotificationToggleAsync from rapid toggle taps.
    // Only one permission flow or schedule/cancel operation should be in flight at a time.
    private bool _isHandlingNotificationToggle;

    /// <summary>
    /// Called from <see cref="PopulateFromUser"/> when the loaded profile has airing notifications
    /// enabled. Requests permission if not yet decided (shows the Android dialog once), or returns
    /// immediately if already granted or denied. On denial, reverts the toggle and shows a message.
    /// MAUI's Permissions.RequestAsync is idempotent — safe to call on every profile load.
    /// </summary>
    private async Task EnsureNotificationPermissionAndScheduleAsync()
    {
        bool granted = await _airingNotificationService.RequestPermissionAsync();
        if (granted)
        {
            // Guard against a race where the user toggled OFF while the permission await was
            // in flight (e.g. a concurrent Settings refresh completing via PopulateFromUser).
            if (AiringNotifications)
            {
                _airingNotificationService.SchedulePeriodicCheck();
            }
        }
        else
        {
            // Cancel any existing WorkManager job — permission was revoked in system settings
            // while the toggle was still ON. Without this, the job keeps running uselessly.
            _airingNotificationService.CancelPeriodicCheck();

            // RequestPermissionAsync uses ConfigureAwait(false) internally, so we may be on a
            // pool thread here. Bound property writes must happen on the UI thread.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _suppressNotificationToggle = true;
                AiringNotifications = false;
                _suppressNotificationToggle = false;
                StatusMessage = "Notification permission is required for airing alerts.";
            });
        }
    }

    private async Task HandleAiringNotificationToggleAsync(bool enabled)
    {
        if (_suppressNotificationToggle || _loadedUser is null)
        {
            return;
        }

        // Rapid toggle taps fire multiple concurrent calls via fire-and-forget.
        // Only one permission flow or schedule/cancel should run at a time — drop the rest.
        if (_isHandlingNotificationToggle)
        {
            return;
        }

        _isHandlingNotificationToggle = true;

        // Cancel any pending debounced save — the permission dialog may take >1500ms to answer,
        // which would fire the save with the pre-dialog toggle value. We'll queue a fresh save
        // after the flow resolves so the persisted value always matches the final outcome.
        _saveDebounceCts?.Cancel();

        try
        {
            if (enabled)
            {
                bool granted = await _airingNotificationService.RequestPermissionAsync();
                if (!granted)
                {
                    // Revert the toggle without re-triggering the handler.
                    // Must stay on the UI thread — AiringNotifications is a bound property.
                    _suppressNotificationToggle = true;
                    AiringNotifications = false;
                    _suppressNotificationToggle = false;

                    // Android won't re-show the system dialog once the user has responded.
                    // Offer to deep-link them directly to the app's notification settings.
                    bool openSettings = await Shell.Current.CurrentPage.DisplayAlertAsync(
                        "Notification Permission Required",
                        "AniSprinkles needs notification permission to alert you when episodes air. Enable it in your device settings, then turn the toggle back on.",
                        "Open Settings",
                        "Cancel");

                    if (openSettings)
                    {
                        AppInfo.Current.ShowSettingsUI();
                    }

                    // Save the reverted (false) value to AniList.
                    TriggerAutoSave();
                    return;
                }

                _airingNotificationService.SchedulePeriodicCheck();

                // If the toggle was flipped back OFF while the permission dialog was open,
                // cancel the job we just scheduled so the final state is consistent.
                if (!AiringNotifications)
                {
                    _airingNotificationService.CancelPeriodicCheck();
                }
            }
            else
            {
                _airingNotificationService.CancelPeriodicCheck();

                // Reset the checkpoint so re-enabling starts fresh — only new episodes
                // going forward, no backlog spam for everything that aired while disabled.
                Preferences.Default.Remove("airing_last_check");
            }

            // Save the final value — granted+scheduled, or cancelled.
            TriggerAutoSave();
        }
        finally
        {
            _isHandlingNotificationToggle = false;
        }
    }

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
        _airingNotificationService.CancelPeriodicCheck();
        _airingNotificationService.ClearNotificationState();
        await _authService.SignOutAsync();
        AppSettings.Clear();
        await RefreshAuthStateAsync();
        ClearUserData();
        CurrentState = PageState.Unauthenticated;
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

        await SaveSettingsAsync();
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
