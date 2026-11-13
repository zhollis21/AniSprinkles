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

    [Test]
    public void Next_AffectsOnlyTheFirstMatchingCall()
    {
        var scope = FaultScope.Next;

        Assert.That(scope.Includes(1), Is.True);
        Assert.That(scope.Includes(2), Is.False);
        Assert.That(scope.Includes(3), Is.False);
    }

    [Test]
    public void Always_AffectsEveryCall()
    {
        var scope = FaultScope.Always;

        Assert.That(scope.Includes(1), Is.True);
        Assert.That(scope.Includes(50), Is.True);
    }

    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, true)]
    [TestCase(6, true)]
    [TestCase(7, false)]
    public void EveryNth_AffectsMultiplesOnly(int hit, bool expected)
    {
        Assert.That(FaultScope.EveryNth(3).Includes(hit), Is.EqualTo(expected));
    }

    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    public void FirstN_AffectsTheLeadingCallsOnly(int hit, bool expected)
    {
        Assert.That(FaultScope.FirstN(2).Includes(hit), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CountedScopes_WithNonPositiveN_MatchNothing(int n)
    {
        // The count arrives from an adb extra, so a typo must disarm the fault rather than take the
        // app down or — worse — silently fault every call via a modulo-by-zero-adjacent surprise.
        Assert.That(FaultScope.EveryNth(n).Includes(1), Is.False);
        Assert.That(FaultScope.FirstN(n).Includes(1), Is.False);
    }

    // ── Arming and matching ───────────────────────────────────────────────────────────────────────

    [Test]
    public void Disarmed_PassesEverythingThrough()
    {
        var state = new FaultState();

        Assert.That(state.Current, Is.Null);
        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough, Is.True);
    }

    [Test]
    public void ArmedForNext_AffectsOneCallThenPassesThrough()
    {
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Next));

        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Client).Kind, Is.EqualTo(ApiErrorKind.ServiceOutage));
        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough, Is.True);
    }

    [Test]
    public void OperationPrefix_MatchesTheAsyncSuffixedMethodName()
    {
        // The whole point of prefix matching: `--es op GetStudio` has to arm GetStudioAsync without
        // anyone having to know the suffix.
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Always, op: "GetStudio"));

        Assert.That(state.Decide("GetStudioAsync", FaultLayer.Client).Kind, Is.EqualTo(ApiErrorKind.ServiceOutage));
        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough, Is.True);
    }

    [Test]
    public void NonMatchingOperations_DoNotAdvanceTheCounter()
    {
        // Otherwise `scope next` armed at GetStudio would be consumed by whatever unrelated call
        // happened to run first, and the fault would never reach the screen it was armed for.
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Next, op: "GetStudio"));

        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough, Is.True);
        Assert.That(state.Decide("GetViewerAsync", FaultLayer.Client).IsPassThrough, Is.True);
        Assert.That(state.Decide("GetStudioAsync", FaultLayer.Client).Kind, Is.EqualTo(ApiErrorKind.ServiceOutage));
    }

    [Test]
    public void AProfileFiresOnlyAtItsOwnLayer()
    {
        // Both seams share one FaultState, so without this a single armed profile would fire twice
        // for one logical call — once at the decorator and once in the HTTP pipeline.
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Always, layer: FaultLayer.Http));

        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough, Is.True);
        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Http).Kind, Is.EqualTo(ApiErrorKind.ServiceOutage));
    }

    [Test]
    public void Clear_Disarms()
    {
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Always));
        state.Clear();

        Assert.That(state.Current, Is.Null);
        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough, Is.True);
    }

    [Test]
    public void ReArming_ResetsTheCounter()
    {
        // Arming the same scope twice must mean the same thing both times; a carried-over counter
        // would make the second `fault ... next` a no-op and look like the receiver had not landed.
        var state = new FaultState();
        state.Arm(Profile(FaultScope.Next));
        state.Decide("GetMediaAsync", FaultLayer.Client);

        state.Arm(Profile(FaultScope.Next));

        Assert.That(state.Decide("GetMediaAsync", FaultLayer.Client).Kind, Is.EqualTo(ApiErrorKind.ServiceOutage));
    }

    [Test]
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

        Assert.That(decision.IsPassThrough, Is.False);
        Assert.That(decision.Kind, Is.Null);
        Assert.That(decision.Delay, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
    }
}
