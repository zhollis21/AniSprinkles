#if DEBUG
namespace AniSprinkles.Services.FaultInjection;

/// <summary>
/// The armed fault, and the counter that decides which matching calls it applies to (#125).
/// Singleton, shared by both seams; disarmed by default.
/// <para>
/// One counter rather than one per operation, deliberately. With a per-operation counter
/// <c>scope next</c> against an unfiltered profile would fail the next call of <em>every</em>
/// operation, which is the whole-interface behaviour <c>FailingAniListClient</c> had and this
/// replaces. A single counter makes <c>Next</c> mean the literal next matching call.
/// </para>
/// <para>
/// Thread model: <see cref="Decide"/> is called from whatever thread a page model happens to be on,
/// and from HTTP continuations on pool threads. Read-modify-write of the counter has to be atomic
/// with the profile read or two concurrent calls could both see hit 1, so both live under one lock.
/// Nothing inside the lock awaits or calls out.
/// </para>
/// </summary>
public sealed class FaultState
{
    private readonly Lock _gate = new();
    private FaultProfile? _profile;
    private int _hits;

    /// <summary>The armed profile, or null when disarmed.</summary>
    public FaultProfile? Current
    {
        get
        {
            lock (_gate)
            {
                return _profile;
            }
        }
    }

    /// <summary>Arms <paramref name="profile"/>, replacing any previous one and resetting the counter.</summary>
    public void Arm(FaultProfile profile)
    {
        lock (_gate)
        {
            _profile = profile;
            _hits = 0;
        }
    }

    /// <summary>Disarms. A cleared state is indistinguishable from a build that never had a fault armed.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _profile = null;
            _hits = 0;
        }
    }

    /// <summary>
    /// What should happen to <paramref name="operation"/> at <paramref name="layer"/>, advancing the
    /// hit counter when the profile matches.
    /// <para>
    /// The counter advances on a <em>match</em>, not on an affected call: that is what makes
    /// <c>EveryNth(3)</c> mean every third matching call rather than every third failure.
    /// </para>
    /// </summary>
    public FaultDecision Decide(string operation, FaultLayer layer)
    {
        lock (_gate)
        {
            if (_profile is not { } profile || profile.Layer != layer || !profile.Matches(operation))
            {
                return FaultDecision.None;
            }

            _hits++;
            return profile.Scope.Includes(_hits)
                ? new FaultDecision(profile.Delay, profile.Kind, profile.AsGraphQlError)
                : FaultDecision.None;
        }
    }
}
#endif
