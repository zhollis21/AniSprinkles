#if CI
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using AniSprinkles.Services.Fixtures;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// The recorded AniList responses a CI build replays, indexed at startup and read on demand (#134).
/// <para>
/// These are real responses captured by <c>tools/record-anilist-fixtures.cs</c> against a dedicated
/// test account. Replaying the raw envelope — rather than hand-building objects, as
/// <c>CIAniListClient</c> used to — means the real <c>AniListClient</c> runs in CI: its
/// deserialization, its paging, its error classification, and the caching decorator above it. A
/// mapping regression now breaks a CI run instead of being invisible to it.
/// </para>
/// <para>
/// <b>Nothing is parsed until it is asked for.</b> This used to deserialize every fixture into
/// JsonNode trees in the constructor, on the main thread, during DI — which is fine at a hundred
/// fixtures and fatal at six hundred: 21 MB of JSON took the app past Android's ~10s startup
/// deadline and it was killed with "failed to complete startup". The cost scaled linearly with the
/// recording, so every re-record walked it closer to the cliff.
/// </para>
/// <para>
/// The fix is that the filename already says everything an index needs. The recorder writes
/// <c>{operation}__{variablesHash}_{queryFingerprint}.json</c>, so the address and the query can be
/// read straight off the resource name without opening it. Startup is now a string split per
/// fixture; a CI run only ever parses the handful of responses the screens actually request.
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

    private readonly Assembly _assembly = Assembly.GetExecutingAssembly();
    private readonly ILogger<FixtureStore> _logger;

    /// <summary>
    /// Address → the recordings at it, each still on disk. A list rather than a single value because
    /// variables do not always identify a request: <c>DiscoverSections</c> sends two different
    /// documents under identical variables, the 18+ aliases being a caller decision rather than a
    /// GraphQL argument. The query fingerprint is what separates them at lookup time.
    /// </summary>
    private readonly Dictionary<string, List<FixtureEntry>> _byKey = new(StringComparer.Ordinal);

    /// <summary>Parsed fixtures, kept after first use so a repeated request costs nothing.</summary>
    private readonly Dictionary<string, GraphQlFixture> _parsed = new(StringComparer.Ordinal);

    private readonly Lock _gate = new();

    public FixtureStore(ILogger<FixtureStore> logger)
    {
        _logger = logger;
        var indexed = 0;

        foreach (var resource in _assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // "AniListFixtures.Media__abc123def456_0a1b2c3d4e5f.json" splits into the address
            // (everything up to the last underscore) and the query fingerprint (after it). Both are
            // written by GraphQlFixtureKey.FileName, so this is reading back what it wrote.
            var stem = resource[ResourcePrefix.Length..];
            if (stem.EndsWith(".json", StringComparison.Ordinal))
            {
                stem = stem[..^".json".Length];
            }

            var split = stem.LastIndexOf('_');
            if (split <= 0 || split == stem.Length - 1)
            {
                _logger.LogError(
                    "FIXTURE resource name is not {{key}}_{{fingerprint}}: {Resource}", resource);
                continue;
            }

            var key = stem[..split];
            var fingerprint = stem[(split + 1)..];

            if (!_byKey.TryGetValue(key, out var atKey))
            {
                atKey = [];
                _byKey[key] = atKey;
            }

            // Same address AND same query is a genuine duplicate — two files that cannot both be
            // right. Distinct fingerprints at one address are expected and are why this is a list.
            if (atKey.Exists(e => string.Equals(e.QueryFingerprint, fingerprint, StringComparison.Ordinal)))
            {
                _logger.LogError(
                    "FIXTURE duplicate at {Key} ({Resource}) — two recordings of the same request and query",
                    key,
                    resource);
                continue;
            }

            atKey.Add(new FixtureEntry(resource, fingerprint));
            indexed++;
        }

        _logger.LogInformation("FIXTURE indexed {Count} recorded response(s)", indexed);
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

        FixtureEntry? match = null;
        if (queryFingerprint is null)
        {
            if (candidates.Count > 1)
            {
                return false;
            }

            match = candidates[0];
        }
        else
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate.QueryFingerprint, queryFingerprint, StringComparison.Ordinal))
                {
                    match = candidate;
                    break;
                }
            }
        }

        if (match is null)
        {
            return false;
        }

        fixture = Load(match.Value.Resource);
        return fixture is not null;
    }

    /// <summary>Whether anything is recorded at this address, whatever the query.</summary>
    public bool HasAny(string key) => _byKey.ContainsKey(key);

    /// <summary>
    /// Reads and parses one fixture, once. Guarded because the replay handler runs on whichever
    /// thread the HTTP pipeline is on, and two screens can ask for the same recording at the same
    /// time — a page and its prefetch, say.
    /// </summary>
    private GraphQlFixture? Load(string resource)
    {
        lock (_gate)
        {
            if (_parsed.TryGetValue(resource, out var cached))
            {
                return cached;
            }

            try
            {
                using var stream = _assembly.GetManifestResourceStream(resource);
                if (stream is null)
                {
                    _logger.LogError("FIXTURE resource has no stream: {Resource}", resource);
                    return null;
                }

                var fixture = JsonSerializer.Deserialize<GraphQlFixture>(stream, ReadOptions);
                if (fixture is null)
                {
                    _logger.LogError("FIXTURE unreadable: {Resource}", resource);
                    return null;
                }

                _parsed[resource] = fixture;
                return fixture;
            }
            catch (JsonException ex)
            {
                // Deliberately not rethrown: the caller turns a null into a FIXTURE MISS, which is
                // logged, fails the CI gate, and leaves the app usable. A parse error taking the
                // capture down would lose every later screenshot to one bad file.
                _logger.LogError(ex, "FIXTURE failed to parse: {Resource}", resource);
                return null;
            }
        }
    }

    private readonly record struct FixtureEntry(string Resource, string QueryFingerprint);
}
#endif
