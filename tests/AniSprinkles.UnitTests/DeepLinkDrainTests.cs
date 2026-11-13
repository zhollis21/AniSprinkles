using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #111. The policy every hook that might follow a queued deep link shares. It used to live twice on
/// the Android side, in copies that had already drifted apart, where nothing could assert that a
/// lifecycle callback never sees an exception or that a missing service is a deferral rather than a
/// failure.
/// </summary>
public class DeepLinkDrainTests
{
    private const string Route = "media-details";

    private static PendingDeepLink Queued(int mediaId = 21, int? nonce = 1)
    {
        var link = new PendingDeepLink(new FakePreferences());
        link.Set(Route, new Dictionary<string, object> { ["mediaId"] = mediaId }, nonce);
        return link;
    }

    [Test]
    public async Task WithEverythingReady_TheLinkIsFollowed()
    {
        var link = Queued();
        var navigation = Substitute.For<INavigationService>();

        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation, shellReady: true), Is.True);

        await navigation.Received(1).GoToAsync(
            Route, false, Arg.Is<IDictionary<string, object>>(p => (int)p["mediaId"] == 21));
    }

    [Test]
    public async Task WithNothingQueued_NavigationIsNotEvenTouched()
    {
        // The common case: a lifecycle callback firing with no link waiting should cost nothing.
        var navigation = Substitute.For<INavigationService>();

        Assert.That(await DeepLinkDrain.AttemptAsync(
            new PendingDeepLink(new FakePreferences()), navigation, shellReady: true), Is.False);

        await navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
    }

    [Test]
    public async Task WithNoPendingDeepLinkService_ItIsANoOp()
    {
        // DI not wired yet. "Too early", not an error — the next hook tries again.
        Assert.That(await DeepLinkDrain.AttemptAsync(
            pending: null, Substitute.For<INavigationService>(), shellReady: true), Is.False);
    }

    [Test]
    public async Task WithNoNavigationService_TheLinkIsKeptForLater()
    {
        var link = Queued();

        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation: null, shellReady: true), Is.False);

        Assert.That(link.HasPending, Is.True);
    }

    [Test]
    public async Task WhenShellIsNotReady_TheLinkIsKeptForLater()
    {
        var link = Queued();
        var navigation = Substitute.For<INavigationService>();

        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation, shellReady: false), Is.False);

        Assert.That(link.HasPending, Is.True);
        await navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
    }

    [Test]
    public async Task WhenNavigationThrows_NothingEscapes_AndTheLinkStaysQueued()
    {
        // The whole reason this is not inlined at each call site: every caller is a lifecycle
        // callback or an event handler, where an escaping exception takes the app down.
        var link = Queued();
        var navigation = Substitute.For<INavigationService>();
        navigation.GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>())
            .Returns(Task.FromException(new InvalidOperationException("route not registered")));

        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation, shellReady: true), Is.False);

        Assert.That(link.HasPending, Is.True);
    }

    [Test]
    public async Task AFailedAttempt_IsFollowedByASuccessfulOne()
    {
        var link = Queued();
        var navigation = Substitute.For<INavigationService>();
        navigation.GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>())
            .Returns(
                _ => Task.FromException(new InvalidOperationException("transient")),
                _ => Task.CompletedTask);

        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation, shellReady: true), Is.False);
        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation, shellReady: true), Is.True);

        Assert.That(link.HasPending, Is.False);
    }

    [Test]
    public async Task RepeatedAttemptsAfterSuccess_DoNothing()
    {
        // All three hooks fire on a normal launch, so this is the ordinary case.
        var link = Queued();
        var navigation = Substitute.For<INavigationService>();

        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation, shellReady: true), Is.True);
        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation, shellReady: true), Is.False);
        Assert.That(await DeepLinkDrain.AttemptAsync(link, navigation, shellReady: true), Is.False);

        await navigation.Received(1).GoToAsync(Route, false, Arg.Any<IDictionary<string, object>>());
    }
}
