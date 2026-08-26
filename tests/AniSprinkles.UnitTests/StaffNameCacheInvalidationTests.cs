using AniSprinkles.Models;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

// NSubstitute verification needs Arg.Any<CancellationToken>() matchers, which conflict with
// xUnit1051's "pass TestContext.Current.CancellationToken" recommendation.
#pragma warning disable xUnit1051

/// <summary>
/// Changing Staff Name Language has to drop the cached entity reads (#130).
/// <para>
/// Names render from AniList's <c>userPreferred</c>, resolved server-side at fetch time, so unlike
/// title language there is nothing local to re-project. <c>CachingAniListClient</c> holds
/// character/staff/studio reads for the process lifetime, so without invalidation the setting would
/// appear to do nothing for the rest of the session — even navigating away and back would re-serve
/// the old rendering from cache.
/// </para>
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class StaffNameCacheInvalidationTests
{
    public StaffNameCacheInvalidationTests() => TestDataBuilder.ResetAppSettings();

    // ── The cache itself ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidateEntityCache_ForcesTheNextReadToHitTheInnerClient()
    {
        var inner = Substitute.For<IAniListClient>();
        inner.GetCharacterAsync(1, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(_ => new Character { Id = 1 });
        var cache = new CachingAniListClient(inner);

        await cache.GetCharacterAsync(1);
        await cache.GetCharacterAsync(1);
        // Two reads, one fetch — the cache is doing its job.
        await inner.Received(1).GetCharacterAsync(1, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        cache.InvalidateEntityCache();
        await cache.GetCharacterAsync(1);

        await inner.Received(2).GetCharacterAsync(1, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateEntityCache_ClearsStaffAndStudioReadsToo()
    {
        // All three entity kinds carry person names, so a partial clear would leave staff or studio
        // pages rendering the previous setting.
        var inner = Substitute.For<IAniListClient>();
        inner.GetStaffAsync(2, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(_ => new Staff { Id = 2 });
        inner.GetStudioAsync(3, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(_ => new Studio { Id = 3 });
        var cache = new CachingAniListClient(inner);

        await cache.GetStaffAsync(2);
        await cache.GetStudioAsync(3);
        cache.InvalidateEntityCache();
        await cache.GetStaffAsync(2);
        await cache.GetStudioAsync(3);

        await inner.Received(2).GetStaffAsync(2, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await inner.Received(2).GetStudioAsync(3, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InvalidateEntityCache_OnAnEmptyCache_IsHarmless()
    {
        var cache = new CachingAniListClient(Substitute.For<IAniListClient>());

        cache.InvalidateEntityCache();
        cache.InvalidateEntityCache();
    }

    // ── Who triggers it ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangingTheSettingInTheApp_InvalidatesTheCache()
    {
        var harness = new Harness();
        await harness.LoadAsync(UserStaffNameLanguage.RomajiWestern);
        harness.Client.ClearReceivedCalls();

        harness.Model.SelectedStaffNameLanguage = UserStaffNameLanguage.Native;

        harness.Client.Received(1).InvalidateEntityCache();
    }

    [Fact]
    public async Task ChangingItUpstream_InvalidatesOnTheNextLoad()
    {
        // The website or another device. This arrives through PopulateFromUser, not the changed
        // handler — which is guarded on _populating and so cannot invalidate. Without this the
        // control would update while cached pages kept the old names until the app restarted.
        var harness = new Harness();
        await harness.LoadAsync(UserStaffNameLanguage.RomajiWestern);
        harness.Client.ClearReceivedCalls();

        harness.ViewerNowReports(UserStaffNameLanguage.Romaji);
        await harness.Model.LoadAsync();

        harness.Client.Received(1).InvalidateEntityCache();
    }

    [Fact]
    public async Task TheFirstLoad_DoesNotInvalidate()
    {
        // The dirty-tracking field starts at the enum default, and _loadedUser is already assigned
        // before PopulateFromUser runs — so comparing against either would drop the cache once per
        // session for anyone whose setting is not RomajiWestern.
        var harness = new Harness();

        await harness.LoadAsync(UserStaffNameLanguage.Native);

        harness.Client.DidNotReceive().InvalidateEntityCache();
    }

    [Fact]
    public async Task ReloadingWithTheSettingUnchanged_DoesNotInvalidate()
    {
        // Settings is a tab; it reloads on every visit. Invalidating on each one would refetch every
        // details page the user goes back to, for nothing.
        var harness = new Harness();
        await harness.LoadAsync(UserStaffNameLanguage.Romaji);
        harness.Client.ClearReceivedCalls();

        await harness.Model.LoadAsync();

        harness.Client.DidNotReceive().InvalidateEntityCache();
    }

    private sealed class Harness
    {
        public Harness()
        {
            Notifications.RequestPermissionAsync().Returns(true);
            Model = new SettingsPageModel(
                Auth,
                Client,
                Notifications,
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Substitute.For<IPreferences>(),
                new ImmediateDispatcher(),
                Substitute.For<IAppInfo>(),
                new ScriptedDialogService(),
                new RecordingUserFeedback(),
                Substitute.For<IExternalBrowser>(),
                NullLogger<SettingsPageModel>.Instance);
        }

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public IAiringNotificationService Notifications { get; } = Substitute.For<IAiringNotificationService>();

        public SettingsPageModel Model { get; }

        public async Task LoadAsync(UserStaffNameLanguage staffNameLanguage)
        {
            Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");
            ViewerNowReports(staffNameLanguage);
            await Model.LoadAsync();
        }

        public void ViewerNowReports(UserStaffNameLanguage staffNameLanguage)
            => Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(new AniListUser
            {
                Id = 1,
                Name = "tester",
                ScoreFormat = ScoreFormat.Point10,
                AnimeSectionOrder = ["Watching"],
                Options = new UserOptions
                {
                    TitleLanguage = UserTitleLanguage.Romaji,
                    StaffNameLanguage = staffNameLanguage,
                    AiringNotifications = true,
                    NotificationOptions = [],
                },
            });
    }
}
