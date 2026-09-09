using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AniSprinkles.Services.Fixtures;

/// <summary>
/// One recorded AniList GraphQL exchange, as it sits on disk (#134).
/// <para>
/// The file is deliberately self-describing rather than a bare response body. A reviewer reading
/// the diff on a re-record needs to see which request produced it, and replay needs the query
/// fingerprint to notice that the app has started asking for fields the recording never answered.
/// </para>
/// </summary>
public sealed class GraphQlFixture
{
    /// <summary>The GraphQL operation name, e.g. <c>MediaListCollection</c>.</summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>
    /// The variables the recording was captured with, verbatim — including the volatile ones
    /// <see cref="GraphQlFixtureKey.VolatileVariables"/> excludes from the key, so the file still
    /// says which season or airing window it came from.
    /// </summary>
    public JsonNode? Variables { get; set; }

    /// <summary>
    /// <see cref="GraphQlFixtureKey.QueryFingerprint"/> of the query at record time. Replay compares
    /// it against the query the app actually sends and fails when they diverge; a stale fixture that
    /// quietly answers a newer query with older fields is precisely the invisible-regression problem
    /// this work exists to remove.
    /// </summary>
    public string QueryFingerprint { get; set; } = string.Empty;

    /// <summary>When the response was captured. Drives the airing-time rebase on replay.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>
    /// The GraphQL response body exactly as AniList returned it, envelope included, so replay can
    /// hand it to the real client unmodified and every mapping and error path runs for real.
    /// </summary>
    public JsonNode? Response { get; set; }

    /// <summary>
    /// How this fixture is addressed by a request that knows its operation and variables. Several
    /// fixtures can share one — see <see cref="FileName"/>.
    /// </summary>
    [JsonIgnore]
    public string Key => GraphQlFixtureKey.Derive(OperationName, Variables);

    /// <summary>The file stem this fixture is stored under; unique.</summary>
    [JsonIgnore]
    public string FileName => GraphQlFixtureKey.FileName(OperationName, Variables, QueryFingerprint);
}
