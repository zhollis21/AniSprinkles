using AniSprinkles.Services.Abstractions;
using AniSprinkles.Services.FaultInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

// NSubstitute setup/verification requires Arg.Any<CancellationToken>() matchers, which inherently
// conflict with xUnit1051's "pass TestContext.Current.CancellationToken" recommendation. Suppress
// it for this file, as CachingAniListClientTests does for the same reason.
#pragma warning disable xUnit1051

/// <summary>
/// Covers the decorator itself (#125) — that it passes through when disarmed, fails only the calls
/// the profile targets, and hands the inner client back afterwards.
/// <para>
/// The pass-through cases matter more than the failure cases. A decorator that breaks everything is
/// what <c>FailingAniListClient</c> already was, and the reason it could not test what it existed to
/// test; the value here is that the fixtures keep serving every call the profile does not name.
/// </para>
/// </summary>
public class FaultInjectingClientTests
{
    private static FaultInjectingAniListClient Decorate(
        IAniListClient inner, FaultState state, IOutageStateService? outage = null)
        => new(
            inner,
            state,
            outage ?? Substitute.For<IOutageStateService>(),
            NullLogger<FaultInjectingAniListClient>.Instance);

    private static FaultProfile Fail(
        ApiErrorKind kind, FaultScope scope, string? op = null, TimeSpan delay = default)
        => new(op, kind, scope, delay, FaultLayer.Client);

    [Fact]
    public async Task Disarmed_DelegatesToTheInnerClient()
    {
        var inner = Substitute.For<IAniListClient>();
        inner.GetMediaAsync(7, Arg.Any<CancellationToken>()).Returns((new Media { Id = 7 }, null));
        var client = Decorate(inner, new FaultState());

        var (media, _) = await client.GetMediaAsync(7);

        Assert.Equal(7, media!.Id);
        await inner.Received(1).GetMediaAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ArmedFault_ThrowsTheClassifiedKindWithoutCallingInner()
    {
        var inner = Substitute.For<IAniListClient>();
        var state = new FaultState();
        state.Arm(Fail(ApiErrorKind.NotFound, FaultScope.Always));
        var client = Decorate(inner, state);

        var ex = await Assert.ThrowsAsync<AniListApiException>(() => client.GetStudioAsync(3));

        Assert.Equal(ApiErrorKind.NotFound, ex.Kind);
        await inner.DidNotReceive().GetStudioAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScopeNext_FailsOnceThenLetsRetrySucceed()
    {
        // This is the scenario the whole feature exists for: proving that Retry actually recovers.
        // With ErrorSim every call failed, so Retry always failed again and the recovery path on all
        // four details pages had never run on a device.
        var inner = Substitute.For<IAniListClient>();
        inner.GetMediaAsync(7, Arg.Any<CancellationToken>()).Returns((new Media { Id = 7 }, null));
        var state = new FaultState();
        state.Arm(Fail(ApiErrorKind.ServiceOutage, FaultScope.Next));
        var client = Decorate(inner, state);

        await Assert.ThrowsAsync<AniListApiException>(() => client.GetMediaAsync(7));
        var (media, _) = await client.GetMediaAsync(7);

        Assert.Equal(7, media!.Id);
        await inner.Received(1).GetMediaAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UntargetedOperations_StillReachTheFixtures()
    {
        // The composition property: a fault armed at one operation must leave every other screen
        // loading normally, or you cannot navigate to the page you wanted to break.
        var inner = Substitute.For<IAniListClient>();
        inner.GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((new Media { Id = 1 }, null));
        var state = new FaultState();
        state.Arm(Fail(ApiErrorKind.ServiceOutage, FaultScope.Always, op: "GetStudio"));
        var client = Decorate(inner, state);

        var (media, _) = await client.GetMediaAsync(1);

        Assert.Equal(1, media!.Id);
        await Assert.ThrowsAsync<AniListApiException>(() => client.GetStudioAsync(1));
    }

    [Fact]
    public async Task DelayOnlyFault_StillReturnsRealData()
    {
        var inner = Substitute.For<IAniListClient>();
        inner.GetMediaAsync(4, Arg.Any<CancellationToken>()).Returns((new Media { Id = 4 }, null));
        var state = new FaultState();
        state.Arm(new FaultProfile(null, null, FaultScope.Always, TimeSpan.FromMilliseconds(1), FaultLayer.Client));
        var client = Decorate(inner, state);

        var (media, _) = await client.GetMediaAsync(4);

        Assert.Equal(4, media!.Id);
    }

    [Fact]
    public async Task DelayedFault_ObservesCancellation()
    {
        // The delay honours the token on purpose — "does navigating away actually stop this?" is the
        // question a delay is armed to answer (#132), and it cannot answer it if the delay ignores
        // cancellation.
        var inner = Substitute.For<IAniListClient>();
        var state = new FaultState();
        state.Arm(new FaultProfile(null, null, FaultScope.Always, TimeSpan.FromSeconds(30), FaultLayer.Client));
        var client = Decorate(inner, state);
        using var cts = new CancellationTokenSource();

        var pending = client.GetMediaAsync(1, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await inner.DidNotReceive().GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ServiceOutageFault_ReportsToTheOutageBanner()
    {
        // Injecting above AniListClient bypasses its own ReportFailure, so the decorator has to do
        // it or the global outage banner never lights up for an injected outage.
        var outage = Substitute.For<IOutageStateService>();
        var state = new FaultState();
        state.Arm(Fail(ApiErrorKind.ServiceOutage, FaultScope.Always));
        var client = Decorate(Substitute.For<IAniListClient>(), state, outage);

        await Assert.ThrowsAsync<AniListApiException>(() => client.GetViewerAsync());

        outage.Received(1).ReportFailure(Arg.Is<AniListApiException>(e => e.Kind == ApiErrorKind.ServiceOutage));
    }

    [Fact]
    public async Task InjectedOutage_ClearsTheBannerOnceACallSucceedsAgain()
    {
        // Caught on device: Retry restored the page to Content with the outage banner still up. The
        // banner is sticky and normally clears via AniListClient's ReportSuccess on the next real
        // round-trip — which never happens behind the CI fixtures, so an injected outage pinned it
        // for the rest of the session and recovery looked broken.
        var outage = Substitute.For<IOutageStateService>();
        var state = new FaultState();
        state.Arm(Fail(ApiErrorKind.ServiceOutage, FaultScope.Next));
        var client = Decorate(Substitute.For<IAniListClient>(), state, outage);

        await Assert.ThrowsAsync<AniListApiException>(() => client.GetViewerAsync());
        await client.GetViewerAsync();

        outage.Received(1).ReportSuccess();
    }

    [Fact]
    public async Task SuccessWithNoInjectedOutage_LeavesTheBannerAlone()
    {
        // The decorator sits above CachingAniListClient and cannot tell a cache hit from a real
        // round-trip, so it must not report success for outages it did not raise — otherwise a cache
        // hit could clear a genuine outage banner in an ordinary Debug session.
        var outage = Substitute.For<IOutageStateService>();
        var client = Decorate(Substitute.For<IAniListClient>(), new FaultState(), outage);

        await client.GetViewerAsync();

        outage.DidNotReceive().ReportSuccess();
    }

    [Fact]
    public async Task NonOutageFault_DoesNotArmTheBannerClear()
    {
        // Only ServiceOutage raises the banner, so only ServiceOutage should later clear it.
        var outage = Substitute.For<IOutageStateService>();
        var state = new FaultState();
        state.Arm(Fail(ApiErrorKind.NotFound, FaultScope.Next));
        var client = Decorate(Substitute.For<IAniListClient>(), state, outage);

        await Assert.ThrowsAsync<AniListApiException>(() => client.GetViewerAsync());
        await client.GetViewerAsync();

        outage.DidNotReceive().ReportSuccess();
    }
}
