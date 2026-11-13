using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #128. A settings auto-save that fails has one durable recovery path — a snackbar the user has to
/// notice and tap within 20 seconds. Miss it and the change never reaches AniList, and is then
/// reverted without a word the next time Settings is opened.
/// <para>
/// <c>c4a2830</c> closed that for <c>DisplayAdultContent</c> by keeping a local change pending until
/// the server confirms it. The marker was scoped to that one field, so every other setting still
/// reverts. These pin the generalised version, plus the two silent-drop paths around it.
/// </para>
/// </summary>
[NonParallelizable]
public class SettingsSaveDurabilityTests
{
    public SettingsSaveDurabilityTests() => TestDataBuilder.ResetAppSettings();

    // ── The revert ───────────────────────────────────────────────────

    [Test]
    public async Task AFailedTitleLanguageSave_SurvivesReopeningSettings()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail();

        harness.Model.SelectedTitleLanguage = UserTitleLanguage.English;
        await harness.Model.FlushPendingSaveAsync();

        // The user comes back later. The viewer still reports the old value, because the save never
        // landed.
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.SelectedTitleLanguage, Is.EqualTo(UserTitleLanguage.English));
        Assert.That(AppSettings.TitleLanguage, Is.EqualTo(UserTitleLanguage.English));
    }

    [Test]
    public async Task AFailedScoreFormatSave_SurvivesReopeningSettings()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail();

        harness.Model.SelectedScoreFormat = ScoreFormat.Point5;
        await harness.Model.FlushPendingSaveAsync();
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.SelectedScoreFormat, Is.EqualTo(ScoreFormat.Point5));
        Assert.That(AppSettings.ScoreFormat, Is.EqualTo(ScoreFormat.Point5));
    }

    [Test]
    public async Task AFailedStaffNameLanguageSave_SurvivesReopeningSettings()
    {
        // Held only by the page model — AppSettings has no entry for it (#130) — so its pending
        // marker has to live there rather than in AppSettings.
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail();

        harness.Model.SelectedStaffNameLanguage = UserStaffNameLanguage.Native;
        await harness.Model.FlushPendingSaveAsync();
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.SelectedStaffNameLanguage, Is.EqualTo(UserStaffNameLanguage.Native));
    }

    [Test]
    public async Task AFailedActivityMergeTimeSave_SurvivesReopeningSettings()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail();

        harness.Model.ActivityMergeTime = 120;
        await harness.Model.FlushPendingSaveAsync();
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.ActivityMergeTime, Is.EqualTo(120));
    }

    [Test]
    public async Task OnceTheServerAgrees_TheSettingFollowsTheServerAgain()
    {
        // The marker must clear, or a change made on the website would never reach this device.
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail();

        harness.Model.SelectedTitleLanguage = UserTitleLanguage.English;
        await harness.Model.FlushPendingSaveAsync();

        // The save eventually lands (or the user changed it on the website): the server now agrees.
        harness.ViewerNowReports(titleLanguage: UserTitleLanguage.English);
        await harness.Model.LoadAsync();

        // A later cross-device change is honoured rather than shadowed forever.
        harness.ViewerNowReports(titleLanguage: UserTitleLanguage.Native);
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.SelectedTitleLanguage, Is.EqualTo(UserTitleLanguage.Native));
    }

    [Test]
    public async Task WithNoLocalChange_TheServerStillWins()
    {
        // The guard must not turn into "the device always wins" — a change made on the website has
        // to arrive.
        var harness = new Harness();
        await harness.LoadAsync();

        harness.ViewerNowReports(titleLanguage: UserTitleLanguage.Native);
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.SelectedTitleLanguage, Is.EqualTo(UserTitleLanguage.Native));
        Assert.That(AppSettings.TitleLanguage, Is.EqualTo(UserTitleLanguage.Native));
    }

    [Test]
    public async Task ASuccessfulSave_LeavesNothingPending()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SelectedTitleLanguage = UserTitleLanguage.English;
        await harness.Model.FlushPendingSaveAsync();

        harness.ViewerNowReports(titleLanguage: UserTitleLanguage.Native);
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.SelectedTitleLanguage, Is.EqualTo(UserTitleLanguage.Native));
    }

    [Test]
    public async Task ASaveTheServerAcceptsButDoesNotEcho_DoesNotResendForever()
    {
        // The failure mode the pending marker could otherwise create: baselining dirty-tracking
        // against the server keeps an unsent change alive, but a server that accepts the request and
        // reports something else back would leave it pending and the page permanently dirty,
        // re-sending a value AniList has already declined on every navigate-away.
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesSucceedButIgnoreTheRequest();

        harness.Model.SelectedTitleLanguage = UserTitleLanguage.English;
        await harness.Model.FlushPendingSaveAsync();

        var sendsAfterFirstSave = harness.Client.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IAniListClient.UpdateUserAsync));

        await harness.Model.FlushPendingSaveAsync();
        await harness.Model.FlushPendingSaveAsync();

        Assert.That(
            harness.Client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAniListClient.UpdateUserAsync)),
            Is.EqualTo(sendsAfterFirstSave));
    }

    // ── Notification settings ────────────────────────────────────────

    [Test]
    public async Task AFailedAiringNotificationsSave_SurvivesReopeningSettings()
    {
        // The worst one to revert: the WorkManager job is already scheduled, so a toggle that flips
        // itself back off leaves the user reading "off" while notifications keep arriving.
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail();

        harness.Model.AiringNotifications = true;
        await harness.Model.FlushPendingSaveAsync();
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.AiringNotifications, Is.True);
    }

    [Test]
    public async Task AFailedPerTypeNotificationSave_SurvivesReopeningSettings()
    {
        // Starts enabled on the server, so turning it off is a real change rather than a no-op.
        var harness = new Harness(notificationTypesEnabled: ["ACTIVITY_LIKE", "AIRING"]);
        await harness.LoadAsync();
        harness.SavesFail();

        harness.Model.NotificationItems.Single(i => i.Type == "ACTIVITY_LIKE").IsEnabled = false;
        await harness.Model.FlushPendingSaveAsync();
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.NotificationItems.Single(i => i.Type == "ACTIVITY_LIKE").IsEnabled, Is.False);

        // Scoped to the type the user touched — the others still follow the server.
        Assert.That(harness.Model.NotificationItems.Single(i => i.Type == "AIRING").IsEnabled, Is.True);
    }

    [Test]
    public async Task APerTypeChangeMadeElsewhere_StillArrives()
    {
        // The guard must stay scoped to the type the user actually touched.
        var harness = new Harness();
        await harness.LoadAsync();

        harness.ViewerNowReports(notificationTypesEnabled: ["ACTIVITY_LIKE"]);
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.NotificationItems.Single(i => i.Type == "ACTIVITY_LIKE").IsEnabled, Is.True);
        Assert.That(harness.Model.NotificationItems.Single(i => i.Type == "AIRING").IsEnabled, Is.False);
    }

    [Test]
    public async Task ASuccessfulNotificationSave_LeavesNothingPending()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.AiringNotifications = true;
        await harness.Model.FlushPendingSaveAsync();

        // A later change made on the website is honoured rather than shadowed forever.
        harness.ViewerNowReports(airingNotifications: false);
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.AiringNotifications, Is.False);
    }

    // ── The orphaned WorkManager job ─────────────────────────────────

    [Test]
    public async Task ReopeningSettings_AfterAiringWasTurnedOffElsewhere_CancelsTheScheduledJob()
    {
        // Independent of saving: turn the setting off on the AniList website, come back here, and
        // the toggle reads off while the job carries on. PopulateFromUser only ever scheduled.
        var harness = new Harness(airingNotifications: true);
        await harness.LoadAsync();
        harness.Notifications.ClearReceivedCalls();

        harness.ViewerNowReports(airingNotifications: false);
        await harness.Model.LoadAsync();

        Assert.That(harness.Model.AiringNotifications, Is.False);
        harness.Notifications.Received().CancelPeriodicCheck();
    }

    [Test]
    public async Task ReopeningSettings_WithAiringStillOn_DoesNotCancelTheJob()
    {
        var harness = new Harness(airingNotifications: true);
        await harness.LoadAsync();
        harness.Notifications.ClearReceivedCalls();

        await harness.Model.LoadAsync();

        harness.Notifications.DidNotReceive().CancelPeriodicCheck();
    }

    // ── The Retry affordance ─────────────────────────────────────────

    [Test]
    public async Task AServiceOutage_OffersNoRetry()
    {
        // Retrying cannot succeed for minutes or hours, and the outage banner already says so.
        // UserFeedbackExtensions drops the action for this kind; this call site did not go through it.
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        harness.Model.SelectedTitleLanguage = UserTitleLanguage.English;
        await harness.Model.FlushPendingSaveAsync();

        Assert.That<Action?>(harness.Feedback.LastSnackbarAction, Is.Null);
    }

    [TestCase(ApiErrorKind.Network)]
    [TestCase(ApiErrorKind.RateLimited)]
    [TestCase(ApiErrorKind.Unknown)]
    public async Task EveryOtherFailureKind_KeepsTheRetry(ApiErrorKind kind)
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail(new AniListApiException(kind, "boom"));

        harness.Model.SelectedTitleLanguage = UserTitleLanguage.English;
        await harness.Model.FlushPendingSaveAsync();

        Assert.That<Action?>(harness.Feedback.LastSnackbarAction, Is.Not.Null);
    }

    [Test]
    public async Task TheSaveFailureSnackbar_StillHoldsForTwentySeconds()
    {
        // The long dwell is the whole recovery path: the user has to notice it to retry.
        var harness = new Harness();
        await harness.LoadAsync();
        harness.SavesFail();

        harness.Model.SelectedTitleLanguage = UserTitleLanguage.English;
        await harness.Model.FlushPendingSaveAsync();

        Assert.That(harness.Feedback.LastSnackbarDuration, Is.EqualTo(TimeSpan.FromSeconds(20)));
    }

    // ── The in-flight drop ───────────────────────────────────────────

    [Test]
    public async Task ASaveArrivingWhileAnotherIsInFlight_IsCoalescedNotDropped()
    {
        // SaveSettingsAsync returned immediately when IsSaving, scheduling nothing — so a Retry tap
        // or a navigate-away flush landing mid-save was discarded, and HasUnsavedChanges staying
        // true did not re-trigger anything.
        var harness = new Harness();
        await harness.LoadAsync();

        var gate = new TaskCompletionSource<AniListUser>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.UpdateUserAsync(Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);

        harness.Model.SelectedTitleLanguage = UserTitleLanguage.English;
        var first = harness.Model.FlushPendingSaveAsync();

        // Mid-save, the user changes something else and navigates away again.
        harness.Model.SelectedScoreFormat = ScoreFormat.Point5;
        var second = harness.Model.FlushPendingSaveAsync();

        harness.AllowSavesToSucceed();
        gate.SetResult(harness.CurrentViewer());
        await Task.WhenAll(first, second);

        await harness.Client.Received().UpdateUserAsync(
            Arg.Is<UpdateUserRequest>(r => r.ScoreFormat == ScoreFormat.Point5), Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        private AniListUser _viewer;

        public Harness(bool airingNotifications = false, string[]? notificationTypesEnabled = null)
        {
            _viewer = Viewer(
                airingNotifications: airingNotifications,
                notificationTypesEnabled: notificationTypesEnabled);

            var dialogs = new ScriptedDialogService();
            Notifications.RequestPermissionAsync().Returns(true);

            Model = new SettingsPageModel(
                Auth,
                Client,
                Notifications,
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Substitute.For<IPreferences>(),
                new ImmediateDispatcher(),
                Substitute.For<IAppInfo>(),
                dialogs,
                Feedback,
                Substitute.For<IExternalBrowser>(),
                NullLogger<SettingsPageModel>.Instance);
        }

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public IAiringNotificationService Notifications { get; } = Substitute.For<IAiringNotificationService>();

        public RecordingUserFeedback Feedback { get; } = new();

        public SettingsPageModel Model { get; }

        public AniListUser CurrentViewer() => _viewer;

        public async Task LoadAsync()
        {
            Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");
            Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(_ => _viewer);
            AllowSavesToSucceed();

            await Model.LoadAsync();
        }

        /// <summary>Repoints the viewer without re-running a load.</summary>
        public void ViewerNowReports(UserTitleLanguage titleLanguage)
            => _viewer = Viewer(titleLanguage);

        public void ViewerNowReports(bool airingNotifications)
            => _viewer = Viewer(
                _viewer.Options.TitleLanguage,
                _viewer.ScoreFormat,
                _viewer.Options.StaffNameLanguage,
                _viewer.Options.ActivityMergeTime,
                airingNotifications);

        public void ViewerNowReports(string[] notificationTypesEnabled)
            => _viewer = Viewer(
                _viewer.Options.TitleLanguage,
                _viewer.ScoreFormat,
                _viewer.Options.StaffNameLanguage,
                _viewer.Options.ActivityMergeTime,
                _viewer.Options.AiringNotifications,
                notificationTypesEnabled);

        public void AllowSavesToSucceed()
            => Client.UpdateUserAsync(Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var request = call.Arg<UpdateUserRequest>();
                    _viewer = Viewer(
                        request.TitleLanguage ?? _viewer.Options.TitleLanguage,
                        request.ScoreFormat ?? _viewer.ScoreFormat,
                        request.StaffNameLanguage ?? _viewer.Options.StaffNameLanguage,
                        request.ActivityMergeTime ?? _viewer.Options.ActivityMergeTime,
                        request.AiringNotifications ?? _viewer.Options.AiringNotifications,
                        request.NotificationOptions is { } sent
                            ? [.. sent.Where(o => o.Enabled).Select(o => o.Type)]
                            : [.. _viewer.Options.NotificationOptions.Where(o => o.Enabled).Select(o => o.Type)]);
                    return Task.FromResult(_viewer);
                });

        /// <summary>The request succeeds, but the viewer comes back unchanged.</summary>
        public void SavesSucceedButIgnoreTheRequest()
            => Client.UpdateUserAsync(Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(_viewer));

        public void SavesFail(Exception? failure = null)
            => Client.UpdateUserAsync(Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>())
                .Returns<Task<AniListUser>>(_ => throw (failure ?? new AniListApiException(ApiErrorKind.Network, "offline")));

        private static AniListUser Viewer(
            UserTitleLanguage titleLanguage = UserTitleLanguage.Romaji,
            ScoreFormat scoreFormat = ScoreFormat.Point100,
            UserStaffNameLanguage staffNameLanguage = UserStaffNameLanguage.RomajiWestern,
            int activityMergeTime = 60,
            bool airingNotifications = false,
            string[]? notificationTypesEnabled = null) => new()
            {
                Id = 1,
                Name = "zhollis",
                ScoreFormat = scoreFormat,
                Options = new UserOptions
                {
                    TitleLanguage = titleLanguage,
                    StaffNameLanguage = staffNameLanguage,
                    ActivityMergeTime = activityMergeTime,
                    AiringNotifications = airingNotifications,
                    NotificationOptions = [.. (notificationTypesEnabled ?? [])
                        .Select(t => new NotificationOption { Type = t, Enabled = true })],
                },
            };
    }
}
