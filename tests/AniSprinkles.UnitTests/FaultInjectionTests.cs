using AniSprinkles.Services.FaultInjection;

namespace AniSprinkles.UnitTests;

/// <summary>
/// Covers the scope arithmetic and arming rules behind fault injection (#125). This is the half of
/// the feature that <em>can</em> be tested off-device: whether a given call is affected is pure
/// bookkeeping, while whether the resulting error state renders correctly is a device question.
/// <para>
/// Worth testing at all because the scopes are the reason the design is deterministic. A fault you
/// cannot re-run is not a test fixture, and an off-by-one in <c>EveryNth</c> is exactly the kind of
/// thing that would quietly make a device pass mean nothing.
/// </para>
/// </summary>
public class FaultInjectionTests
{
    private static FaultProfile Profile(
        FaultScope scope,
        string? op = null,
        ApiErrorKind? kind = ApiErrorKind.ServiceOutage,
        FaultLayer layer = FaultLayer.Client)
        => new(op, kind, scope, TimeSpan.Zero, layer);

    // ── Scope arithmetic ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Next_AffectsOnlyTheFirstMatchingCall()
    {
        var scope = FaultScope.Next;

        Assert.True(scope.Includes(1));
        Assert.False(scope.Includes(2));
        Assert.False(scope.Includes(3));
    }

    [Fact]
    public void Always_AffectsEveryCall()
    {
        var scope = FaultScope.Always;

        Assert.True(scope.Includes(1));
        Assert.True(scope.Includes(50));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void EveryNth_AffectsMultiplesOnly(int hit, bool expected)
    {
        Assert.Equal(expected, FaultScope.EveryNth(3).Includes(hit));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void FirstN_AffectsTheLeadingCallsOnly(int hit, bool expected)
    {
        Assert.Equal(expected, FaultScope.FirstN(2).Includes(hit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CountedScopes_WithNonPositiveN_MatchNothing(int n)
    {
        // The count arrives from an adb extra, so a typo must disarm the fault rather than take the
        // app down or — worse — silently fault every call via a modulo-by-zero-adjacent surprise.
        Assert.False(FaultScope.EveryNth(n).Includes(1));
        Assert.False(FaultScope.FirstN(n).Includes(1));
    }

    // ── Arming and matching ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Disarmed_PassesEverythingThrough()
    {
        var state = new FaultState();

        Assert.Null(state.Current);
        Assert.True(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough);
    }

    [Fact]
    public void ArmedForNext_AffectsOneCallThenPassesThrough()
    {
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Next));

        Assert.Equal(ApiErrorKind.ServiceOutage, state.Decide("GetMediaAsync", FaultLayer.Client).Kind);
        Assert.True(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough);
    }

    [Fact]
    public void OperationPrefix_MatchesTheAsyncSuffixedMethodName()
    {
        // The whole point of prefix matching: `--es op GetStudio` has to arm GetStudioAsync without
        // anyone having to know the suffix.
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Always, op: "GetStudio"));

        Assert.Equal(ApiErrorKind.ServiceOutage, state.Decide("GetStudioAsync", FaultLayer.Client).Kind);
        Assert.True(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough);
    }

    [Fact]
    public void NonMatchingOperations_DoNotAdvanceTheCounter()
    {
        // Otherwise `scope next` armed at GetStudio would be consumed by whatever unrelated call
        // happened to run first, and the fault would never reach the screen it was armed for.
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Next, op: "GetStudio"));

        Assert.True(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough);
        Assert.True(state.Decide("GetViewerAsync", FaultLayer.Client).IsPassThrough);
        Assert.Equal(ApiErrorKind.ServiceOutage, state.Decide("GetStudioAsync", FaultLayer.Client).Kind);
    }

    [Fact]
    public void AProfileFiresOnlyAtItsOwnLayer()
    {
        // Both seams share one FaultState, so without this a single armed profile would fire twice
        // for one logical call — once at the decorator and once in the HTTP pipeline.
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Always, layer: FaultLayer.Http));

        Assert.True(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough);
        Assert.Equal(ApiErrorKind.ServiceOutage, state.Decide("GetMediaAsync", FaultLayer.Http).Kind);
    }

    [Fact]
    public void Clear_Disarms()
    {
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Always));
        state.Clear();

        Assert.Null(state.Current);
        Assert.True(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough);
    }

    [Fact]
    public void ReArming_ResetsTheCounter()
    {
        // Arming the same scope twice must mean the same thing both times; a carried-over counter
        // would make the second `fault ... next` a no-op and look like the receiver had not landed.
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Next));
        state.Decide("GetMediaAsync", FaultLayer.Client);

        state.Arm(Profile(FaultScope.Next));

        Assert.Equal(ApiErrorKind.ServiceOutage, state.Decide("GetMediaAsync", FaultLayer.Client).Kind);
    }

    [Fact]
    public void DelayOnlyProfile_IsNotAPassThrough()
    {
        // A fault with no error kind is legitimate and load-bearing: it is the only way to open the
        // cancellation window #132 lives in, and it must survive the pass-through short-circuit.
        var state = new FaultState();
        state.Arm(new FaultProfile(
            OperationPrefix: null,
            Kind: null,
            Scope: FaultScope.Always,
            Delay: TimeSpan.FromMilliseconds(500),
            Layer: FaultLayer.Client));

        var decision = state.Decide("GetMediaAsync", FaultLayer.Client);

        Assert.False(decision.IsPassThrough);
        Assert.Null(decision.Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(500), decision.Delay);
    }
}
