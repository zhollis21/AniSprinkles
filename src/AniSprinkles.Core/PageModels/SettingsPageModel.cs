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
    private readonly ErrorReportService _errorReportService;
    private readonly IPreferences _preferences;
    private readonly IDispatcher _dispatcher;
    private readonly IAppInfo _appInfo;
    private readonly IDialogService _dialogs;
    private readonly IUserFeedback _feedback;
    private readonly IExternalBrowser _browser;
    private readonly ILogger<SettingsPageModel> _logger;

    // Snapshot of the loaded state for dirty-tracking
    private AniListUser? _loadedUser;
    private UserTitleLanguage _loadedTitleLanguage;
    private UserStaffNameLanguage _loadedStaffNameLanguage;

    /// <summary>
    /// The staff name language the last viewer sync reported, or null before the first one. Distinct
    /// from <see cref="_loadedStaffNameLanguage"/>, which is dirty-tracking and starts at the enum
    /// default — this one has to be able to say "never synced" so an upstream change can be told
    /// apart from a first populate (#130).
    /// </summary>
    private UserStaffNameLanguage? _lastSyncedStaffNameLanguage;
    private ScoreFormat _loadedScoreFormat;
    private bool _loadedDisplayAdultContent;
    private bool _loadedAiringNotifications;
    private bool _loadedRestrictMessages;
    private int _loadedActivityMergeTime;

    /// <summary>
    /// True while <see cref="PopulateFromUser"/> is assigning server state, so the change handlers
    /// can tell "the user chose this" from "the server reported this".
    /// </summary>
    private bool _populating;

    /// <summary>
    /// Locally changed but not yet confirmed by AniList (#128), for the settings this page model
    /// holds on its own. Their <c>AppSettings</c>-backed counterparts keep their markers there,
    /// because <c>MyAnimePageModel</c> calls <c>SyncFromViewer</c> directly and has to honour them
    /// without going through this page model at all.
    /// </summary>
    private bool _staffNameLanguageAwaitingUpstream;
    private bool _restrictMessagesAwaitingUpstream;
    private bool _activityMergeTimeAwaitingUpstream;
    private bool _airingNotificationsAwaitingUpstream;

    /// <summary>
    /// The per-type notification toggles the user has changed but AniList has not confirmed. A set
    /// rather than a flag because these are a list — shadowing the whole list would stop a change
    /// made on another device from arriving for any type, not just the one that is unsent.
    /// </summary>
    private readonly HashSet<string> _notificationTypesAwaitingUpstream = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set when a save is requested while one is already in flight, so the in-flight save runs one
    /// more pass instead of the request being dropped (#128).
    /// </summary>
    private bool _saveRequestedWhileSaving;

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
    private string _aniListUserId = string.Empty;

    /// <summary>
    /// True when the singleton ViewModel already has authenticated profile data
    /// available for immediate display (e.g. after a tab switch).
    /// </summary>
    public bool HasLoadedData => _loadedUser is not null;

    [ObservableProperty]
    private bool _isSaving;

    // Drives SfPullToRefresh.IsRefreshing; also tracked for guards while LoadAsync is in flight.
    [ObservableProperty]
    private bool _isBusy;

    // ── Error state (full-page error view) ──────────────────────────
    // Visibility is driven by CurrentState == PageState.Error; the following
    // properties populate the error view template.
    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorSubtitle = string.Empty;

    [ObservableProperty]
    private string _errorIconGlyph = string.Empty;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

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

    // --- Section expanded states ---
    [ObservableProperty]
    private bool _isDisplayPreferencesExpanded = true;

    [ObservableProperty]
    private bool _isContentPrivacyExpanded = true;

    [ObservableProperty]
    private bool _isNotificationsExpanded = false;

    [ObservableProperty]
    private bool _isAccountExpanded = true;

    [RelayCommand]
    private void ToggleDisplayPreferences() => IsDisplayPreferencesExpanded = !IsDisplayPreferencesExpanded;

    [RelayCommand]
    private void ToggleContentPrivacy() => IsContentPrivacyExpanded = !IsContentPrivacyExpanded;

    [RelayCommand]
    private void ToggleNotifications() => IsNotificationsExpanded = !IsNotificationsExpanded;

    [RelayCommand]
    private void ToggleAccount() => IsAccountExpanded = !IsAccountExpanded;

    // --- App info ---
    // Injected rather than read from the AppInfo.Current static: that throws
    // NotImplementedInReferenceAssemblyException off-device, and in a field initializer it took the
    // whole page model with it — this type could not even be constructed in a test. Registered
    // alongside IPreferences in MauiProgram, same as the seams PR #61 added.
    public string AppVersion { get; }

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

    public SettingsPageModel(IAuthService authService, IAniListClient aniListClient, IAiringNotificationService airingNotificationService, ErrorReportService errorReportService, IPreferences preferences, IDispatcher dispatcher, IAppInfo appInfo, IDialogService dialogs, IUserFeedback feedback, IExternalBrowser browser, ILogger<SettingsPageModel> logger)
    {
        _authService = authService;
        _aniListClient = aniListClient;
        _airingNotificationService = airingNotificationService;
        _errorReportService = errorReportService;
        _preferences = preferences;
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _appInfo = appInfo;
        AppVersion = $"v{appInfo.VersionString}";
        _feedback = feedback;
        _browser = browser;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        // Only show the spinner for the initial load (no cached data).
        // On refresh-with-cached-data the content view is already visible;
        // flipping CurrentState would overlay the spinner on top of it.
        var isRefresh = _loadedUser is not null;
        _logger.LogInformation(
            "Settings LoadAsync enter (isRefresh={IsRefresh}, isBusy={IsBusy}, currentState={CurrentState}, isAuthenticated={IsAuthenticated})",
            isRefresh, IsBusy, CurrentState, IsAuthenticated);

        if (IsBusy)
        {
            _logger.LogInformation("Settings LoadAsync skipped — already in flight.");
            return;
        }

        // Set IsBusy immediately — before any awaits — so concurrent callers
        // (OnAppearing + pull-to-refresh, or rapid Retry taps) short-circuit above.
        IsBusy = true;

        if (!isRefresh)
        {
            CurrentState = PageState.InitialLoading;
        }

        try
        {
            // Inner try: if the auth check itself throws (e.g. SecureStorage failure),
            // ensure IsAuthenticated is false so the outer catch routes to Unauthenticated
            // rather than the full-page Error state.
            try
            {
                await RefreshAuthStateAsync();
            }
            catch
            {
                IsAuthenticated = false;
                throw;
            }

            if (IsAuthenticated)
            {
                // ConfigureAwait(true) here and on the other awaits in this class whose continuation
                // writes bound state — the same convention as DetailsPageModelBase and
                // PaginatedSection. It is the default, so this changes nothing at runtime; it states
                // the requirement. PopulateFromUser assigns dozens of [ObservableProperty] values and
                // rebuilds the bound NotificationItems collection, all of which must happen on the UI
                // thread, and nothing else in this class would fail visibly if that stopped holding.
                var user = await _aniListClient.GetViewerAsync().ConfigureAwait(true);
                _loadedUser = user;
                PopulateFromUser(user);
                ErrorDetails = string.Empty;
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
            var apiEx = ex as AniListApiException;

            if (_loadedUser is not null)
            {
                // Prefer stale data over blank UI when refresh fails after a previously successful load.
                // Pull-to-refresh is the retry path, so no action on the snackbar.
                await _feedback.ShowSnackbarAsync(apiEx?.UserTitle ?? "Refresh failed. Showing cached profile.");
                CurrentState = PageState.Content;
            }
            else if (IsAuthenticated)
            {
                // Full-page error state — no cached data to fall back on.
                ErrorTitle = apiEx?.UserTitle ?? "Something Went Wrong";
                ErrorSubtitle = apiEx?.UserSubtitle ?? "An unexpected error occurred. Try again or check back later.";
                ErrorIconGlyph = apiEx?.IconGlyph ?? Glyphs.Regular.ErrorCircle24;
                CurrentState = PageState.Error;
            }
            else
            {
                // Auth check itself failed; fall back to Unauthenticated so the user
                // can retry sign-in from the login card.
                await _feedback.ShowSnackbarAsync(apiEx?.UserTitle ?? "Failed to load profile.");
                CurrentState = PageState.Unauthenticated;
            }

            ErrorDetails = _errorReportService.Record(ex, "Load Settings");
        }
        finally
        {
            IsBusy = false;
        }

        SentrySdk.AddBreadcrumb("Settings loaded", "navigation", "state");
    }

    /// <param name="savedRequest">
    /// The request this response is the reply to, when <paramref name="user"/> came back from our own
    /// <c>UpdateUser</c> rather than from a plain load. The server has now ruled on the values it
    /// carried, so their pending markers clear even if the response did not echo them back.
    /// <para>
    /// Without that, a server which quietly declined a value would leave it pending and the page
    /// permanently dirty, re-sending the same rejected change on every navigate-away. A load is the
    /// opposite case: it asks a server that may simply not have received the save yet, so a pending
    /// change has to survive it (#128).
    /// </para>
    /// <para>
    /// Compared field by field against the current value, because the user can change a setting while
    /// a save is in flight — that change was never sent, and clearing its marker would discard it.
    /// </para>
    /// </param>
    private void PopulateFromUser(AniListUser user, UpdateUserRequest? savedRequest = null)
    {
        if (savedRequest is not null)
        {
            AppSettings.ConfirmSettingsSaved(
                savedRequest.TitleLanguage ?? AppSettings.TitleLanguage,
                savedRequest.ScoreFormat ?? AppSettings.ScoreFormat,
                savedRequest.DisplayAdultContent ?? AppSettings.DisplayAdultContent);

            ConfirmIfSent(ref _staffNameLanguageAwaitingUpstream, savedRequest.StaffNameLanguage, SelectedStaffNameLanguage);
            ConfirmIfSent(ref _restrictMessagesAwaitingUpstream, savedRequest.RestrictMessagesToFollowing, RestrictMessagesToFollowing);
            ConfirmIfSent(ref _activityMergeTimeAwaitingUpstream, savedRequest.ActivityMergeTime, ActivityMergeTime);
            ConfirmIfSent(ref _airingNotificationsAwaitingUpstream, savedRequest.AiringNotifications, AiringNotifications);

            foreach (var sent in savedRequest.NotificationOptions ?? [])
            {
                var current = NotificationItems.FirstOrDefault(i =>
                    string.Equals(i.Type, sent.Type, StringComparison.OrdinalIgnoreCase));

                if (current is not null && current.IsEnabled == sent.Enabled)
                {
                    _notificationTypesAwaitingUpstream.Remove(sent.Type);
                }
            }
        }

        // Tells the change handlers that what follows is the server reporting, not the user
        // choosing — so none of these assignments marks a setting as a pending local change.
        // Reset in a finally: a flag stuck on would silently stop tracking every later change.
        _populating = true;
        try
        {
            PopulateCore(user);
        }
        finally
        {
            _populating = false;
        }
    }

    private void PopulateCore(AniListUser user)
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

        // Display preferences.
        //
        // Every one of these is resolved rather than taken straight from the viewer (#128). A save
        // that failed — or is still in flight — leaves the server reporting the old value, and
        // assigning it blind reverted the user's explicit choice the next time Settings opened, with
        // nothing said. Only the adult toggle was protected before this.
        SelectedTitleLanguage = AppSettings.ResolveTitleLanguage(user.Options.TitleLanguage);
        SelectedScoreFormat = AppSettings.ResolveScoreFormat(user.ScoreFormat);
        SelectedStaffNameLanguage = PendingValue.Resolve(
            ref _staffNameLanguageAwaitingUpstream, user.Options.StaffNameLanguage, SelectedStaffNameLanguage);

        // Content & Privacy.
        //
        // Resolved rather than taken straight from the viewer: this assignment runs the changed
        // handler, which writes through to AppSettings. Returning to Settings before our own save
        // landed — or after it failed — would otherwise stamp the server's stale value over the
        // choice the user just made, before SyncFromViewer's guard below saw it at all.
        DisplayAdultContent = AppSettings.ResolveDisplayAdultContent(user.Options.DisplayAdultContent);
        AiringNotifications = PendingValue.Resolve(
            ref _airingNotificationsAwaitingUpstream, user.Options.AiringNotifications, AiringNotifications);
        RestrictMessagesToFollowing = PendingValue.Resolve(
            ref _restrictMessagesAwaitingUpstream, user.Options.RestrictMessagesToFollowing, RestrictMessagesToFollowing);

        // Activity merge time
        ActivityMergeTime = PendingValue.Resolve(
            ref _activityMergeTimeAwaitingUpstream, user.Options.ActivityMergeTime, ActivityMergeTime);

        // Notifications
        PopulateNotificationItems(user.Options.NotificationOptions);

        // Snapshot for dirty-tracking — against what the SERVER reported, not against the resolved
        // values assigned above (#128). Where the two differ the change is still unsent, and
        // baselining against the local copy would mark the page clean and quietly stop trying: the
        // choice would survive on this device and never reach AniList. Recording the server's value
        // instead leaves the page dirty, so the existing debounce and the navigate-away flush
        // re-send it. Identical to the old behaviour whenever nothing is pending.
        _loadedTitleLanguage = user.Options.TitleLanguage;

        // A staff-name-language change made somewhere else — the website, another device — arrives
        // here rather than through the changed handler, which is guarded on _populating and so does
        // not invalidate (#130). Without this the control would update while every cached
        // character/staff/studio page kept rendering names under the previous setting, until the app
        // restarted.
        //
        // Tracked in its own nullable rather than compared against _loadedStaffNameLanguage: that one
        // starts at default(RomajiWestern), and _loadedUser is already assigned before this method
        // runs, so neither can tell a first populate from a genuine upstream change. Null here means
        // "never synced", which is the only reliable way to avoid dropping the cache once per session
        // for anyone whose setting is not the default.
        if (_lastSyncedStaffNameLanguage is { } previous && user.Options.StaffNameLanguage != previous)
        {
            _logger.LogInformation(
                "Staff name language changed upstream ({Old} → {New}) — dropping cached entity reads",
                previous,
                user.Options.StaffNameLanguage);
            _aniListClient.InvalidateEntityCache();
        }

        _lastSyncedStaffNameLanguage = user.Options.StaffNameLanguage;
        _loadedStaffNameLanguage = user.Options.StaffNameLanguage;
        _loadedScoreFormat = user.ScoreFormat;
        _loadedDisplayAdultContent = user.Options.DisplayAdultContent;
        _loadedAiringNotifications = user.Options.AiringNotifications;
        _loadedRestrictMessages = user.Options.RestrictMessagesToFollowing;
        _loadedActivityMergeTime = user.Options.ActivityMergeTime;

        // Sync local app settings from user profile. SyncFromViewer resolves the pending markers
        // itself — each clears only when the server reports the value we are holding, so a save reply
        // confirms it while a load against a server that has not caught up does not.
        AppSettings.SyncFromViewer(user);

        _suppressNotificationToggle = false;

        // Re-enable WorkManager if the user has airing notifications enabled.
        // Check permission first — existing users may have the toggle ON from AniList
        // but haven't granted POST_NOTIFICATIONS on this device yet.
        if (AiringNotifications)
        {
            _ = EnsureNotificationPermissionAndScheduleAsync();
        }
        else
        {
            // And cancel it when it is off. Without this the job outlived the toggle: turning
            // airing notifications off on the AniList website (or anywhere else) left this device
            // still scheduled, so the switch read off while notifications kept arriving. Only the
            // enabling half was ever wired here. Cancelling an unscheduled job is a no-op, so this
            // is safe to run on every load.
            _airingNotificationService.CancelPeriodicCheck();
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void PopulateNotificationItems(List<NotificationOption> options)
    {
        // Captured before the rebuild below throws the items away. A type the user toggled but whose
        // save has not landed keeps its local value; every other type follows the server (#128).
        var unconfirmed = NotificationItems
            .Where(i => _notificationTypesAwaitingUpstream.Contains(i.Type))
            .ToDictionary(i => i.Type, i => i.IsEnabled, StringComparer.OrdinalIgnoreCase);

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
            var serverValue = lookup.TryGetValue(type, out var val) && val;

            // Same rule as PendingValue.Resolve, per type: the server wins unless this one is still
            // unconfirmed and the server disagrees, in which case the local choice stands and stays
            // pending. It clears when the server reports what we are holding.
            var enabled = serverValue;
            if (unconfirmed.TryGetValue(type, out var localValue))
            {
                if (localValue != serverValue)
                {
                    enabled = localValue;
                }
                else
                {
                    _notificationTypesAwaitingUpstream.Remove(type);
                }
            }

            var item = new NotificationToggleItem(type, displayName, category, enabled);

            // Constructed with the value rather than assigned through the setter, so this handler
            // cannot fire during population — only a real user toggle reaches it.
            item.PropertyChanged += (_, _) =>
            {
                _notificationTypesAwaitingUpstream.Add(type);
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

        // Sign-out must not leave the previous account's unconfirmed changes shadowing the next
        // viewer's real preferences — the same reason AppSettings.Clear resets its own markers.
        _notificationTypesAwaitingUpstream.Clear();

        // Forget the synced value too, so the next account is treated as a first populate rather
        // than compared against the previous viewer's setting (#130).
        _lastSyncedStaffNameLanguage = null;
        _staffNameLanguageAwaitingUpstream = false;
        _restrictMessagesAwaitingUpstream = false;
        _activityMergeTimeAwaitingUpstream = false;
        _airingNotificationsAwaitingUpstream = false;

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

    partial void OnSelectedTitleLanguageChanged(UserTitleLanguage value)
    {
        // Commit locally first, for the same reason the adult toggle does (#118, widened in #128).
        // Before this the value reached AppSettings only when a save succeeded, so the choice took
        // 1.5 s to reach the rest of the app and was lost entirely if the save failed.
        if (!_populating)
        {
            AppSettings.SetTitleLanguage(value);
        }

        TriggerAutoSave();
    }

    partial void OnSelectedScoreFormatChanged(ScoreFormat value)
    {
        if (!_populating)
        {
            AppSettings.SetScoreFormat(value);
        }

        TriggerAutoSave();
    }

    partial void OnSelectedStaffNameLanguageChanged(UserStaffNameLanguage value)
    {
        MarkPendingUnlessPopulating(ref _staffNameLanguageAwaitingUpstream);

        // Drop the cached character/staff/studio reads (#130). Unlike title language, this setting
        // cannot be re-projected: names render from AniList's `userPreferred`, which is resolved
        // server-side against this setting at fetch time, so everything already in the session cache
        // is stale the moment it moves. Nothing is refetched here — pages reload as the user
        // navigates back to them, which spreads the cost over screens actually visited instead of
        // firing a burst on a settings tap. Guarded on _populating so merely opening Settings, which
        // assigns this from the server, does not throw the cache away.
        if (!_populating)
        {
            _aniListClient.InvalidateEntityCache();
        }

        TriggerAutoSave();
    }

    partial void OnDisplayAdultContentChanged(bool value)
    {
        // Commit locally first, ahead of the debounced AniList save (#118). Every browse surface
        // filters on AppSettings.DisplayAdultContent and checks it when it appears, so waiting for
        // the round-trip left a second-and-a-half window where the user believed 18+ was off and
        // the app still thought it was on.
        //
        // This also runs while PopulateFromUser assigns the server's value, which is correct — that
        // is the same value SyncFromViewer commits moments later. TriggerAutoSave is a no-op there
        // in practice: it may queue a debounce, but PopulateFromUser updates the dirty-tracking
        // snapshot before the delay elapses, so DebouncedSaveAsync re-checks HasUnsavedChanges and
        // finds nothing to send.
        AppSettings.SetDisplayAdultContent(value);
        TriggerAutoSave();
    }
    partial void OnAiringNotificationsChanged(bool value)
    {
        // Marked outside the suppress guard below, unlike the save it triggers. The permission-denied
        // path also assigns here — suppressed, so the handler is skipped — and that reverted false is
        // just as much a local decision the server has not seen. Its own explicit TriggerAutoSave is
        // what sends it (#128).
        MarkPendingUnlessPopulating(ref _airingNotificationsAwaitingUpstream);

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
    partial void OnRestrictMessagesToFollowingChanged(bool value)
    {
        MarkPendingUnlessPopulating(ref _restrictMessagesAwaitingUpstream);
        TriggerAutoSave();
    }

    partial void OnActivityMergeTimeChanged(int value)
    {
        MarkPendingUnlessPopulating(ref _activityMergeTimeAwaitingUpstream);
        TriggerAutoSave();
    }

    /// <summary>
    /// Marks a setting as locally changed and unconfirmed — unless the assignment came from
    /// <see cref="PopulateFromUser"/>, which is the server telling us what it holds rather than the
    /// user choosing anything.
    /// <para>
    /// The suppression is load-bearing here in a way it is not for the <c>AppSettings</c>-backed
    /// settings above: those get a second pass through <c>SyncFromViewer</c> at the end of
    /// <c>PopulateFromUser</c>, which clears a marker the server already agrees with. These have no
    /// such pass, so a marker set while populating would stay set — and shadow a genuine cross-device
    /// change forever.
    /// </para>
    /// </summary>
    private void MarkPendingUnlessPopulating(ref bool awaitingUpstream)
    {
        if (!_populating)
        {
            awaitingUpstream = true;
        }
    }

    /// <summary>
    /// Clears a pending marker when the save that just succeeded carried the value still being held.
    /// A mismatch means the user changed it while that save was in flight, so it is genuinely still
    /// unsent and stays pending. See the <c>savedRequest</c> parameter on <see cref="PopulateFromUser"/>.
    /// </summary>
    private static void ConfirmIfSent<T>(ref bool awaitingUpstream, T? sentValue, T currentValue)
        where T : struct
    {
        if (sentValue is { } sent && EqualityComparer<T>.Default.Equals(sent, currentValue))
        {
            awaitingUpstream = false;
        }
    }

    private void TriggerAutoSave()
    {
        if (_loadedUser is null || !HasUnsavedChanges)
        {
            return;
        }

        _ = DebouncedSaveAsync();
    }

    /// <summary>
    /// Sends a pending settings change immediately instead of waiting out the debounce. Called from
    /// <c>SettingsPage.OnDisappearing</c>, so a change made and navigated away from within 1500 ms
    /// is not left unsent — and is not lost outright if the app is killed before the delay elapses.
    /// </summary>
    /// <remarks>
    /// A no-op when nothing is dirty, which is the common case: OnDisappearing fires on every tab
    /// away, and flushing must not turn tab switching into an AniList write.
    /// </remarks>
    public async Task FlushPendingSaveAsync()
    {
        _saveDebounceCts?.Cancel();

        if (_loadedUser is null || !HasUnsavedChanges)
        {
            return;
        }

        await SaveSettingsAsync();
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
            // Coalesce rather than drop (#128). This used to return having scheduled nothing, so a
            // Retry tap or a navigate-away flush landing mid-save vanished — and HasUnsavedChanges
            // staying true does not re-trigger a save on its own.
            _saveRequestedWhileSaving = true;
            return;
        }

        IsSaving = true;
        try
        {
            bool succeeded;
            do
            {
                _saveRequestedWhileSaving = false;
                succeeded = await SendSettingsAsync();
            }

            // Only after a success. A failed attempt has already surfaced its snackbar, and looping
            // on failure would retry instantly and repeatedly against a server that just refused.
            while (succeeded && _saveRequestedWhileSaving && HasUnsavedChanges);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Sends the current settings once. Returns false when the attempt failed.</summary>
    private async Task<bool> SendSettingsAsync()
    {
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

            var updatedUser = await _aniListClient.UpdateUserAsync(request).ConfigureAwait(true);
            _loadedUser = updatedUser;
            PopulateFromUser(updatedUser, savedRequest: request);
            SentrySdk.AddBreadcrumb("Settings auto-saved", "settings", "user");
            _dispatcher.Dispatch(() => _ = _feedback.ShowToastAsync("Settings saved"));
            return true;
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;
            _errorReportService.Record(ex, "Auto-save settings");

            // Transient auto-save failures surface via a Snackbar with a Retry action.
            // The persistent inline-banner redesign (notification-permission warning,
            // refresh-failed-showing-cache, etc.) is tracked in issue #26.
            //
            // The change itself is not lost either way: its pending marker keeps it ahead of the
            // server's stale copy, and the dirty-tracking baseline recorded in PopulateFromUser
            // leaves the page dirty so the next flush re-sends it (#128).
            var message = apiEx?.UserTitle ?? "Couldn't save settings.";
            _dispatcher.Dispatch(() => _ = ShowSaveFailureSnackbarAsync(ex, message));
            return false;
        }
    }

    /// <summary>
    /// Surfaces a failed save. Held for 20 seconds rather than the default: a settings save that
    /// failed is only recoverable if the user actually notices the Retry.
    /// </summary>
    /// <remarks>
    /// Routed through <c>ShowFailureSnackbarAsync</c> so a <c>ServiceOutage</c> drops the Retry
    /// action — it cannot succeed for minutes or hours, and the outage banner already says so. This
    /// call site built its own snackbar and so kept offering a Retry that could not work (#128).
    /// </remarks>
    private Task ShowSaveFailureSnackbarAsync(Exception exception, string message)
        => _feedback.ShowFailureSnackbarAsync(
            exception,
            message,
            retryAction: () => _ = SaveSettingsAsync(),
            duration: TimeSpan.FromSeconds(20));

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
        bool granted = await _airingNotificationService.RequestPermissionAsync().ConfigureAwait(true);
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

            // Dispatched defensively, not because the continuation is known to be off the UI thread.
            // The await above captures this method's SynchronizationContext, and every caller starts
            // on the UI thread, so it resumes there — RequestPermissionAsync's own internal
            // ConfigureAwait(false) governs continuations inside that method and does not propagate
            // out to this one. Keeping the Dispatch costs nothing and the snackbar below genuinely
            // requires the UI thread, so both stay.
            // Revert the toggle silently on the bound thread, then explicitly queue persistence —
            // _suppressNotificationToggle bypasses OnAiringNotificationsChanged and its normal
            // autosave path, so without TriggerAutoSave() the reverted false value never reaches
            // AniList and the next profile load would re-enable the toggle again.
            _dispatcher.Dispatch(() =>
            {
                _suppressNotificationToggle = true;
                AiringNotifications = false;
                _suppressNotificationToggle = false;
                TriggerAutoSave();
            });

            // Dispatch the snackbar to the UI thread so Snackbar.Show() runs on the main thread
            // as required by the MAUI alert layer.
            _dispatcher.Dispatch(() => _ = _feedback.ShowSnackbarAsync(
                "Notification permission is required for airing alerts.",
                "Open Settings",
                () => _appInfo.ShowSettingsUI(),
                TimeSpan.FromSeconds(10)));
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
                bool granted = await _airingNotificationService.RequestPermissionAsync().ConfigureAwait(true);
                if (!granted)
                {
                    SentrySdk.AddBreadcrumb("Notification permission denied", "notification", "user");

                    // Revert the toggle without re-triggering the handler.
                    // Must stay on the UI thread — AiringNotifications is a bound property.
                    _suppressNotificationToggle = true;
                    AiringNotifications = false;
                    _suppressNotificationToggle = false;

                    // Android won't re-show the system dialog once the user has responded.
                    // Offer to deep-link them directly to the app's notification settings.
                    bool openSettings = await _dialogs.ConfirmAsync(
                        title: "Notification Permission Required",
                        message: "AniSprinkles needs notification permission to alert you when episodes air. Enable it in your device settings, then turn the toggle back on.",
                        confirmText: "Open Settings",
                        iconGlyph: Glyphs.Regular.Alert24);

                    SentrySdk.AddBreadcrumb(
                        openSettings ? "Permission settings prompt: opened settings" : "Permission settings prompt: dismissed",
                        "notification",
                        "user");

                    if (openSettings)
                    {
                        _appInfo.ShowSettingsUI();
                    }

                    // Save the reverted (false) value to AniList.
                    TriggerAutoSave();
                    return;
                }

                SentrySdk.AddBreadcrumb("Notification permission granted", "notification", "user");
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
                _preferences.Remove("airing_last_check");
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
            var signedIn = await _authService.SignInAsync().ConfigureAwait(true);
            await RefreshAuthStateAsync();

            if (signedIn)
            {
                await _feedback.ShowToastAsync("Signed in to AniList.");
                await LoadAsync();
            }
            else
            {
                await _feedback.ShowToastAsync("Sign in canceled.");
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
            await _feedback.ShowSnackbarAsync("Sign in failed. Try again.", "Retry", () => _ = SignIn());
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        SentrySdk.AddBreadcrumb("Sign-out confirmation shown (Settings)", "auth", "user");
        var confirmed = await _dialogs.ConfirmAsync(
            title: "Sign Out",
            message: "Sign out of AniList? Your list data will be cleared from the app until you sign back in.",
            confirmText: "Sign Out",
            isDestructive: true);

        if (!confirmed)
        {
            SentrySdk.AddBreadcrumb("Sign-out canceled (Settings)", "auth", "user");
            return;
        }

        _logger.LogInformation("Sign-out confirmed from Settings.");
        SentrySdk.AddBreadcrumb("Sign-out confirmed (Settings)", "auth", "user");
        _airingNotificationService.CancelPeriodicCheck();
        _airingNotificationService.ClearNotificationState();
        await _authService.SignOutAsync();
        AppSettings.Clear();
        await RefreshAuthStateAsync();
        ClearUserData();
        CurrentState = PageState.Unauthenticated;
        await _feedback.ShowToastAsync("Signed out.");
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
            await _browser.OpenAsync(new Uri(SiteUrl));
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
            await _browser.OpenAsync(new Uri("https://anilist.co/settings"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open AniList settings URL");
        }
    }

    private async Task RefreshAuthStateAsync()
    {
        var token = await _authService.GetAccessTokenAsync().ConfigureAwait(true);
        IsAuthenticated = !string.IsNullOrWhiteSpace(token);
    }

    // ── Pull to refresh ──────────────────────────────────────────────

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    // ── Retry after full-page error ─────────────────────────────────

    [RelayCommand]
    private async Task RetryLoad()
    {
        ErrorTitle = string.Empty;
        ErrorSubtitle = string.Empty;
        ErrorIconGlyph = string.Empty;
        ErrorDetails = string.Empty;
        CurrentState = PageState.InitialLoading;
        await LoadAsync();
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
