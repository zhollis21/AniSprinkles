using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #111. The decision half of deep-linking a notification tap: what is pending, when it is safe to
/// follow, and when a tap has already been followed. The Android side — reading the intent extra,
/// the lifecycle hooks that ask for a drain — can't be tested off-device, so everything that can
/// live here does.
/// </summary>
public class PendingDeepLinkTests
{
    private const string Route = "media-details";

    private static Dictionary<string, object> Media(int id) => new() { ["mediaId"] = id };

    private static PendingDeepLink WithPending(int mediaId, int nonce = 1, FakePreferences? store = null)
    {
        var link = new PendingDeepLink(store ?? new FakePreferences());
        link.Set(Route, Media(mediaId), nonce);
        return link;
    }

    [Fact]
    public async Task WhenShellIsReady_TheLinkIsFollowedOnce()
    {
        var link = WithPending(21);
        var navigation = Substitute.For<INavigationService>();

        Assert.True(await link.TryNavigateAsync(navigation, shellReady: true));

        await navigation.Received(1).GoToAsync(
            Route, false, Arg.Is<IDictionary<string, object>>(p => (int)p["mediaId"] == 21));
        Assert.False(link.HasPending);
    }

    [Fact]
    public async Task WhenShellIsNotReady_NothingNavigates_AndTheLinkSurvives()
    {
        // The silent-loss regression. MauiShellNavigationService returns a completed task when
        // Shell.Current is null, so clearing on "GoToAsync returned" rather than on readiness would
        // swallow every cold-start deep link.
        var link = WithPending(21);
        var navigation = Substitute.For<INavigationService>();

        Assert.False(await link.TryNavigateAsync(navigation, shellReady: false));

        await navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
        Assert.True(link.HasPending);
    }

    [Fact]
    public async Task ALinkThatWaited_IsFollowedOnTheNextAttempt()
    {
        var link = WithPending(21);
        var navigation = Substitute.For<INavigationService>();

        await link.TryNavigateAsync(navigation, shellReady: false);
        Assert.True(await link.TryNavigateAsync(navigation, shellReady: true));

        await navigation.Received(1).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task DrainingAgainAfterSuccess_DoesNothing()
    {
        // Three hooks ask for a drain — intent arrival, Shell's Navigated, and OnResume — so
        // repeated attempts are the normal case, not an edge case.
        var link = WithPending(21);
        var navigation = Substitute.For<INavigationService>();

        await link.TryNavigateAsync(navigation, shellReady: true);
        Assert.False(await link.TryNavigateAsync(navigation, shellReady: true));

        await navigation.Received(1).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task WithNothingPending_DrainingIsANoOp()
    {
        var navigation = Substitute.For<INavigationService>();

        Assert.False(await new PendingDeepLink(new FakePreferences()).TryNavigateAsync(navigation, shellReady: true));

        await navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
    }

    [Fact]
    public async Task TwoTapsBeforeEitherIsFollowed_FollowTheSecond()
    {
        var link = new PendingDeepLink(new FakePreferences());
        var navigation = Substitute.For<INavigationService>();

        link.Set(Route, Media(21), nonce: 1);
        link.Set(Route, Media(16498), nonce: 2);
        await link.TryNavigateAsync(navigation, shellReady: true);

        await navigation.Received(1).GoToAsync(
            Route, false, Arg.Is<IDictionary<string, object>>(p => (int)p["mediaId"] == 16498));
        await navigation.DidNotReceive().GoToAsync(
            Route, false, Arg.Is<IDictionary<string, object>>(p => (int)p["mediaId"] == 21));
    }

    // ── Navigation failure ──────────────────────────────────────────

    [Fact]
    public async Task WhenNavigationThrows_TheLinkSurvivesAndIsRetried()
    {
        // The user tapped a notification and never arrived. Losing the link here would be the same
        // silent nothing-happens as every other failure on this path.
        var store = new FakePreferences();
        var link = WithPending(21, nonce: 4242, store);
        var navigation = Substitute.For<INavigationService>();
        navigation.GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>())
            .Returns(Task.FromException(new InvalidOperationException("route not registered")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => link.TryNavigateAsync(navigation, shellReady: true));

        Assert.True(link.HasPending);

        // And the nonce was not burned, so a re-delivered intent still counts as the same live tap.
        Assert.True(link.Set(Route, Media(21), nonce: 4242));
    }

    [Fact]
    public async Task AfterAFailedNavigation_TheNextAttemptSucceeds()
    {
        var link = WithPending(21, nonce: 4242);
        var navigation = Substitute.For<INavigationService>();
        navigation.GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>())
            .Returns(
                _ => Task.FromException(new InvalidOperationException("transient")),
                _ => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => link.TryNavigateAsync(navigation, shellReady: true));
        Assert.True(await link.TryNavigateAsync(navigation, shellReady: true));

        // Now it is consumed, so the replayed intent is ignored.
        Assert.False(link.Set(Route, Media(21), nonce: 4242));
    }

    [Fact]
    public async Task WhenNavigationThrowsAfterANewerTapArrived_TheNewerTapWins()
    {
        // Restoring unconditionally would resurrect the stale link over the one the user most
        // recently asked for.
        var link = WithPending(21, nonce: 1);
        var navigation = Substitute.For<INavigationService>();
        int calls = 0;
        navigation.GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>())
            .Returns(_ =>
            {
                if (++calls > 1)
                {
                    return Task.CompletedTask;
                }

                // Arrives while the first navigation is in flight — past the lock, so Set can take it.
                link.Set(Route, Media(16498), nonce: 2);
                return Task.FromException(new InvalidOperationException("boom"));
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => link.TryNavigateAsync(navigation, shellReady: true));

        await link.TryNavigateAsync(navigation, shellReady: true);

        await navigation.Received(1).GoToAsync(
            Route, false, Arg.Is<IDictionary<string, object>>(p => (int)p["mediaId"] == 16498));
    }

    // ── Replay ──────────────────────────────────────────────────────

    [Fact]
    public async Task AReplayedTap_IsIgnored()
    {
        // Android re-delivers the original intent when it recreates an activity. Without this, a
        // cold-start deep link would re-navigate on every recreation, pulling the user off whatever
        // they had browsed to since.
        var link = WithPending(21, nonce: 4242);
        var navigation = Substitute.For<INavigationService>();
        await link.TryNavigateAsync(navigation, shellReady: true);

        Assert.False(link.Set(Route, Media(21), nonce: 4242));

        Assert.False(link.HasPending);
        Assert.False(await link.TryNavigateAsync(navigation, shellReady: true));
        await navigation.Received(1).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task AGenuineSecondTapOfADifferentEpisode_IsNotTreatedAsAReplay()
    {
        // Notification ids are per (media, episode), so a new episode of the same show is a
        // different nonce and must still navigate.
        var link = WithPending(21, nonce: 4242);
        var navigation = Substitute.For<INavigationService>();
        await link.TryNavigateAsync(navigation, shellReady: true);

        Assert.True(link.Set(Route, Media(21), nonce: 9999));
        Assert.True(await link.TryNavigateAsync(navigation, shellReady: true));

        await navigation.Received(2).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task AReplayArrivingBeforeTheFirstDrain_StillLeavesOneNavigation()
    {
        var link = WithPending(21, nonce: 4242);
        var navigation = Substitute.For<INavigationService>();

        // Same intent seen twice while still pending — not yet followed, so it stays queued once.
        Assert.True(link.Set(Route, Media(21), nonce: 4242));
        await link.TryNavigateAsync(navigation, shellReady: true);

        await navigation.Received(1).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task AReplayAfterProcessDeath_IsStillIgnored()
    {
        // Verified on device: killing the process and restoring from recents rebuilds the task from
        // the intent that *created* it, in a new process, with its extras intact — several taps
        // later. Our own Intent.RemoveExtra can't reach the system's copy, so the guard has to
        // outlive the process. A fresh instance over the same store stands in for the restart.
        var store = new FakePreferences();
        var navigation = Substitute.For<INavigationService>();

        var beforeDeath = WithPending(21, nonce: 4242, store);
        await beforeDeath.TryNavigateAsync(navigation, shellReady: true);

        var afterRestore = new PendingDeepLink(store);
        Assert.False(afterRestore.Set(Route, Media(21), nonce: 4242));

        Assert.False(afterRestore.HasPending);
        Assert.False(await afterRestore.TryNavigateAsync(navigation, shellReady: true));
        await navigation.Received(1).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task AFreshTapAfterProcessDeath_StillNavigates()
    {
        var store = new FakePreferences();
        var navigation = Substitute.For<INavigationService>();

        var beforeDeath = WithPending(21, nonce: 4242, store);
        await beforeDeath.TryNavigateAsync(navigation, shellReady: true);

        var afterRestore = new PendingDeepLink(store);
        Assert.True(afterRestore.Set(Route, Media(16498), nonce: 9999));
        Assert.True(await afterRestore.TryNavigateAsync(navigation, shellReady: true));

        await navigation.Received(2).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task ANonceOfZero_IsARealNonce_NotAnAbsentOne()
    {
        // The nonce is a hash, so 0 is a value it can legitimately take — rare, but a notification
        // that drew it must still get replay protection rather than silently losing it. "Absent" is
        // null; the two are distinguished by key presence in storage, not by a sentinel value.
        var link = WithPending(21, nonce: 0);
        var navigation = Substitute.For<INavigationService>();
        await link.TryNavigateAsync(navigation, shellReady: true);

        Assert.False(link.Set(Route, Media(21), nonce: 0));

        await navigation.Received(1).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task ANonceOfZero_SurvivesProcessDeathLikeAnyOther()
    {
        var store = new FakePreferences();
        var navigation = Substitute.For<INavigationService>();

        var beforeDeath = WithPending(21, nonce: 0, store);
        await beforeDeath.TryNavigateAsync(navigation, shellReady: true);

        var afterRestore = new PendingDeepLink(store);

        Assert.False(afterRestore.Set(Route, Media(21), nonce: 0));
        await navigation.Received(1).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task WithoutANonce_ReplayProtectionIsOff()
    {
        // null means "nothing to deduplicate on" — not 0, which the tests above cover as an ordinary
        // nonce. Two such taps both navigate.
        var link = new PendingDeepLink(new FakePreferences());
        var navigation = Substitute.For<INavigationService>();

        link.Set(Route, Media(21), nonce: null);
        await link.TryNavigateAsync(navigation, shellReady: true);
        Assert.True(link.Set(Route, Media(21), nonce: null));
        await link.TryNavigateAsync(navigation, shellReady: true);

        await navigation.Received(2).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }
}
