#if CI
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using AniSprinkles.Services.Fixtures;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// The recorded AniList responses a CI build replays, loaded once from embedded resources (#134).
/// <para>
/// These are real responses captured by <c>tools/record-anilist-fixtures.cs</c> against a dedicated
/// test account. Replaying the raw envelope — rather than hand-building objects, as
/// <c>CIAniListClient</c> used to — means the real <c>AniListClient</c> runs in CI: its
/// deserialization, its paging, its error classification, and the caching decorator above it. A
/// mapping regression now breaks a CI run instead of being invisible to it.
/// </para>
/// </summary>
internal sealed class FixtureStore : IFixtureLookup
{
    /// <summary>Matches the <c>LogicalName</c> the csproj gives each embedded fixture.</summary>
    private const string ResourcePrefix = "AniListFixtures.";

    /// <summary>Must match the recorder's writer, or nothing deserializes.</summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Address → recordings at that address. A list rather than a single value because variables do
    /// not always identify a request: <c>DiscoverSections</c> sends two different documents under
    /// identical variables, the 18+ aliases being a caller decision rather than a GraphQL argument.
    /// The query fingerprint is what separates them at lookup time.
    /// </summary>
    private readonly Dictionary<string, List<GraphQlFixture>> _byKey = new(StringComparer.Ordinal);

    public FixtureStore(ILogger<FixtureStore> logger)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var embedded = 0;

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            embedded++;

            try
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null)
                {
                    logger.LogError("FIXTURE resource has no stream: {Resource}", name);
                    continue;
                }

                var fixture = JsonSerializer.Deserialize<GraphQlFixture>(stream, ReadOptions);
                if (fixture is null)
                {
                    logger.LogWarning("FIXTURE unreadable: {Resource}", name);
                    continue;
                }

                if (!_byKey.TryGetValue(fixture.Key, out var atKey))
                {
                    atKey = [];
                    _byKey[fixture.Key] = atKey;
                }

                // Same address AND same query is a genuine duplicate — two files that cannot both be
                // right. Distinct fingerprints at one address are expected and are the reason this
                // is a list.
                if (atKey.Exists(f => string.Equals(f.QueryFingerprint, fixture.QueryFingerprint, StringComparison.Ordinal)))
                {
                    logger.LogError(
                        "FIXTURE duplicate at {Key} ({Resource}) — two recordings of the same request and query",
                        fixture.Key,
                        name);
                    continue;
                }

                atKey.Add(fixture);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "FIXTURE failed to parse: {Resource}", name);
            }
        }

        // Both numbers, always. A fixture that is embedded but does not load is invisible until some
        // unrelated screen reports a miss, and "509 of 511" is the difference between noticing that
        // in the log and rediscovering it a month later from a failing CI run.
        var loaded = Count;
        if (loaded == embedded)
        {
            logger.LogInformation("FIXTURE loaded {Count} recorded response(s)", loaded);
        }
        else
        {
            logger.LogError(
                "FIXTURE loaded only {Loaded} of {Embedded} embedded response(s) — {Missing} did not load",
                loaded,
                embedded,
                embedded - loaded);
        }
    }

    public int Count => _byKey.Values.Sum(v => v.Count);

    /// <summary>
    /// The recording for <paramref name="key"/> whose query matches <paramref name="queryFingerprint"/>.
    /// <para>
    /// A null fingerprint means "the caller knows the request but not the query text" — as when the
    /// mutation synthesizer borrows a media block from a recorded <c>Media</c> response. That resolves
    /// only when the address is unambiguous, which is the honest answer: with two candidates and no
    /// way to choose, guessing would be worse than missing.
    /// </para>
    /// </summary>
    public bool TryGet(
        string key, string? queryFingerprint, [NotNullWhen(true)] out GraphQlFixture? fixture)
    {
        fixture = null;

        if (!_byKey.TryGetValue(key, out var candidates) || candidates.Count == 0)
        {
            return false;
        }

        if (queryFingerprint is null)
        {
            if (candidates.Count > 1)
            {
                return false;
            }

            fixture = candidates[0];
            return true;
        }

        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.QueryFingerprint, queryFingerprint, StringComparison.Ordinal))
            {
                fixture = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether anything is recorded at this address, whatever the query.</summary>
    public bool HasAny(string key) => _byKey.ContainsKey(key);
}
#endif
