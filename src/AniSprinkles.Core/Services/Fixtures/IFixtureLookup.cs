#if DEBUG
using System.Diagnostics.CodeAnalysis;

namespace AniSprinkles.Services.Fixtures;

/// <summary>
/// Read access to the recorded AniList responses (#134).
/// <para>
/// Exists so the logic that <em>uses</em> fixtures can live here in Core, where tests can reach it,
/// while the thing that <em>loads</em> them stays in the app project — loading means enumerating
/// embedded resources out of the Android assembly, which is not something Core can or should do.
/// </para>
/// <para>
/// Guarded on <c>DEBUG</c> rather than <c>CI</c> for a practical reason: <c>dotnet test</c> builds
/// Core in its ordinary Debug configuration, with no <c>CiBuild</c>, so a <c>CI</c> guard would
/// compile this out of the very build the tests link against — the same coverage gap, moved. DEBUG
/// keeps it out of Release, which is the guarantee that matters, and matches how fault injection
/// already ships (see AGENTS.md).
/// </para>
/// </summary>
public interface IFixtureLookup
{
    /// <summary>
    /// The recording at <paramref name="key"/> whose query matches <paramref name="queryFingerprint"/>.
    /// A null fingerprint means "any", and resolves only when the address is unambiguous.
    /// </summary>
    bool TryGet(string key, string? queryFingerprint, [NotNullWhen(true)] out GraphQlFixture? fixture);
}
#endif
