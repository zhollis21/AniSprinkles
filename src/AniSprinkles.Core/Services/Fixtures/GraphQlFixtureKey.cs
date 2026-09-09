using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace AniSprinkles.Services.Fixtures;

/// <summary>
/// How a recorded AniList GraphQL response is addressed on disk (#134).
/// <para>
/// The recorder (<c>tools/record-anilist-fixtures.cs</c>) and the CI replay handler both derive
/// keys through here, and neither has its own copy. If they ever disagreed, a recording would land
/// under one name and be looked up under another — which fails as "fixture missing" at the exact
/// moment the fixtures are supposed to be proving something, so the shared implementation is the
/// point of this type rather than an incidental tidiness.
/// </para>
/// </summary>
public static class GraphQlFixtureKey
{
    /// <summary>
    /// Variables deliberately excluded from the key, per operation.
    /// <para>
    /// These are derived from the wall clock or from the viewer's current list, so they differ on
    /// every run and would make a recorded fixture unfindable the moment the calendar moved on.
    /// <c>DiscoverSections</c> sends the current and next season plus their years (computed by
    /// <c>AniListSeason</c>), and <c>AiringSchedule</c> sends a unix-timestamp window plus the media
    /// ids drawn from the cached library. Keying on any of them would mean a fixture recorded in
    /// autumn stopped resolving in winter — the run would fail loudly rather than silently, but it
    /// would fail for a reason that has nothing to do with the code under test.
    /// </para>
    /// <para>
    /// The values a fixture was captured with are still written into the file, so the recording is
    /// self-describing even though the key ignores them.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> VolatileVariables { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["DiscoverSections"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "season", "seasonYear", "nextSeason", "nextSeasonYear",
            },
            ["AiringSchedule"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "mediaIds", "airingAfter", "airingBefore",
            },
            // The Airing and Upcoming "View All" pages pin the current and next season the same way
            // the Discover query does, so they go stale for the same reason. Dropping the season
            // still leaves them distinguishable: Airing sends status RELEASING and Upcoming sends
            // NOT_YET_RELEASED, and every other browse section sends no season at all. If a section
            // is ever added that differs from an existing one *only* by season, it would collide
            // onto one fixture — which is why that pair is spelled out here rather than left to be
            // rediscovered later.
            ["BrowseAnime"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "season", "seasonYear",
            },
        };

    /// <summary>
    /// How a request is *addressed*: the operation name, which keeps the directory browsable, plus a
    /// short digest of the variables that select the response.
    /// <para>
    /// Deliberately not unique on its own — see <see cref="FileName"/>. This is what a caller can
    /// compute when it knows which request it wants but not which query text produced it.
    /// </para>
    /// </summary>
    public static string Derive(string operationName, JsonNode? variables)
        => $"{operationName}__{ShortHash(CanonicalizeVariables(operationName, variables))}";

    /// <summary>
    /// The file a recording is stored under: the address plus the query's fingerprint.
    /// <para>
    /// The query has to be part of the filename because variables alone do not identify a request.
    /// <c>GetDiscoverSectionsAsync</c> is the proof: it sends two different documents — one with the
    /// 18+ aliases, one without — under identical variables, because which document to send is a
    /// caller decision rather than a GraphQL argument. Keyed on variables alone the two collapse onto
    /// one file, the second silently overwrites the first, and replay then answers whichever query
    /// the app sends with the other one's response.
    /// </para>
    /// </summary>
    public static string FileName(string operationName, JsonNode? variables, string queryFingerprint)
        => $"{Derive(operationName, variables)}_{queryFingerprint}";

    /// <summary>
    /// The variables reduced to one stable string: keys sorted at every depth so property order
    /// cannot change the key, array order preserved because in GraphQL it is meaningful, and the
    /// operation's volatile names dropped entirely.
    /// </summary>
    public static string CanonicalizeVariables(string operationName, JsonNode? variables)
    {
        var volatileNames = VolatileVariables.TryGetValue(operationName, out var names)
            ? names
            : null;

        var canonical = Canonicalize(variables, volatileNames, depth: 0);
        return canonical?.ToJsonString() ?? "null";
    }

    /// <summary>
    /// A digest of the query text itself, stored alongside the response so replay can tell that the
    /// app now asks for something the recording never answered.
    /// <para>
    /// Whitespace is collapsed first, so reformatting a query does not invalidate every fixture it
    /// produced — only a real change to the requested fields does.
    /// </para>
    /// </summary>
    public static string QueryFingerprint(string query)
        => ShortHash(CollapseWhitespace(query));

    private static JsonNode? Canonicalize(JsonNode? node, IReadOnlySet<string>? volatileNames, int depth)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                // Volatile names are stripped at the top level only. Nested objects are response
                // shapes or filter bags where the same name can mean something entirely different,
                // and dropping it there would silently merge two distinct requests onto one key.
                var strip = depth == 0 ? volatileNames : null;

                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (strip?.Contains(property.Key) == true)
                    {
                        continue;
                    }

                    result[property.Key] = Canonicalize(property.Value, volatileNames, depth + 1);
                }

                return result;
            }

            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var item in array)
                {
                    result.Add(Canonicalize(item, volatileNames, depth + 1));
                }

                return result;
            }

            default:
                return node?.DeepClone();
        }
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string ShortHash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
}
