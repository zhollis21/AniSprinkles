using AniSprinkles.Services.FaultInjection;

namespace AniSprinkles.UnitTests;

/// <summary>
/// <see cref="FaultState.Decide"/> is a read-modify-write on the hit counter, and every scope's
/// meaning depends on that being atomic — <c>next</c> must fire exactly once no matter how many
/// callers race, and <c>firstn:N</c> must affect exactly N.
/// <para>
/// Callers arrive from any thread: page models on the UI thread, HTTP continuations on pool threads
/// (<c>AniListClient.SendAsync</c> uses <c>ConfigureAwait(false)</c>). A lost increment would let
/// more calls than the scope allows slip under the threshold.
/// </para>
/// </summary>
public class FaultStateConcurrencyTests
{
    /// <summary>
    /// The gate is <c>System.Threading.Lock</c>, which is a sealed <em>class</em> — C# 13 lowers
    /// <c>lock</c> on it to <c>EnterScope()</c> rather than <c>Monitor.Enter</c> on a boxed copy.
    /// Pinned because "that boxes per use, so it synchronizes nothing" is a plausible-sounding
    /// review comment, and the answer should be checkable rather than argued.
    /// </summary>
    [Fact]
    public void TheGateType_IsAReferenceType()
    {
        Assert.False(typeof(Lock).IsValueType);
    }

    [Fact]
    public async Task ScopeNext_FiresExactlyOnceUnderContention()
    {
        var state = new FaultState();
        state.Arm(new FaultProfile(null, ApiErrorKind.ServiceOutage, FaultScope.Next, TimeSpan.Zero, FaultLayer.Client));

        var affected = await CountAffectedAsync(state, callers: 64);

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task FirstN_AffectsExactlyNUnderContention()
    {
        // The discriminating case. A lost increment leaves the counter behind the number of calls,
        // so MORE than N callers would still see a hit under the threshold and be affected.
        var state = new FaultState();
        state.Arm(new FaultProfile(null, ApiErrorKind.NotFound, FaultScope.FirstN(10), TimeSpan.Zero, FaultLayer.Client));

        var affected = await CountAffectedAsync(state, callers: 200);

        Assert.Equal(10, affected);
    }

    [Fact]
    public async Task EveryNth_AffectsTheExactExpectedShareUnderContention()
    {
        var state = new FaultState();
        state.Arm(new FaultProfile(null, ApiErrorKind.Network, FaultScope.EveryNth(4), TimeSpan.Zero, FaultLayer.Client));

        var affected = await CountAffectedAsync(state, callers: 200);

        Assert.Equal(50, affected);
    }

    /// <summary>Releases every caller at once so they genuinely contend, then counts the faulted ones.</summary>
    private static async Task<int> CountAffectedAsync(FaultState state, int callers)
    {
        using var start = new SemaphoreSlim(0, callers);
        var affected = 0;

        var work = Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            await start.WaitAsync(TestContext.Current.CancellationToken);
            if (!state.Decide("GetMediaAsync", FaultLayer.Client).IsPassThrough)
            {
                Interlocked.Increment(ref affected);
            }
        })).ToArray();

        start.Release(callers);
        await Task.WhenAll(work);

        return Volatile.Read(ref affected);
    }
}
