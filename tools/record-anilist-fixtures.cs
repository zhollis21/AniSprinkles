// Records real AniList GraphQL responses into checked-in fixtures for the CI build (#134).
//
// The CI build does not talk to AniList. It used to answer every call from hand-written C# objects
// in CIAniListClient, which made whole behaviours invisible: paging methods returned an empty list
// unconditionally, so a details-page sort change emptied the section, and no screenshot could ever
// catch it. This tool records what AniList actually returns; the replay handler in the app serves
// those bytes back to the *real* AniListClient, so mapping, caching, paging and error classification
// all run for real in CI against data that is real too.
//
// Why record rather than query live from the workflow:
//   - CI commits and diffs screenshots. Live data moves daily (airing countdowns, popularity,
//     averageScore), so every run would diff and the signal would drown.
//   - The adult-content canary must be guaranteed present and guaranteed filtered. Live data cannot
//     promise that.
//   - GitHub runner IPs are shared, and AniList rate-limits per IP.
//
// Usage (from the repo root):
//   $env:ANILIST_RECORDER_TOKEN = "<token for the dedicated test account>"
//   dotnet run tools/record-anilist-fixtures.cs
//   dotnet run tools/record-anilist-fixtures.cs -- --max-media 20
//   dotnet run tools/record-anilist-fixtures.cs -- --out <dir> --force --spacing-ms 2200 --max-pages 3
//
// --max-media caps how many list entries get media fixtures, and the recorded list is trimmed to
// match. That is what lets the test account mirror a real library — useful for driving the app by
// hand — while CI carries a bounded, self-consistent slice of it.
//
// The token is read from the environment and never written to disk, echoed, or committed. Use the
// dedicated test account, not a personal one: everything this records lands in a public repo.
//
// Recording is resumable, and a resume is cheap. Each response is written the moment it arrives, and
// a request whose fixture already exists is answered from disk without going to AniList at all
// (unless --force), so a run interrupted by a 429 costs only the fixtures that had not landed yet
// rather than spending the whole rate-limit budget again. Requests that do go out are spaced through
// the app's own AniListRateLimitHandler, which honours Retry-After, so the tool throttles itself the
// same way the app does.

// The file-based-app default turns on the trim/AOT analysers, which flag the reflection-based
// JsonSerializer calls below. This tool is run by hand from the repo root and is never published,
// let alone AOT-compiled, so the analysis has nothing to say here.
#:property PublishAot=false
#:property IsAotCompatible=false
#:project ../src/AniSprinkles.Core/AniSprinkles.Core.csproj

using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AniSprinkles.Models;
using AniSprinkles.PageModels;
using AniSprinkles.Services;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.Services.Fixtures;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

var token = ReadToken();
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("""
        No token found — neither ANILIST_RECORDER_TOKEN nor tmp/dev-token.txt.

        The easy way is to let the app do the OAuth flow for you: sign in on the emulator as the
        test account, then

            pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 dump-token

        which writes tmp/dev-token.txt (gitignored), and this tool reads it from there.

        Otherwise put a token in the environment rather than on the command line, so it does not
        land in shell history:

            $env:ANILIST_RECORDER_TOKEN = "<token>"      # PowerShell
            export ANILIST_RECORDER_TOKEN="<token>"      # bash

        Do not use a personal account. Everything recorded here is committed to a public repo.
        """);
    return 1;
}

var options = RecorderOptions.Parse(args);
Console.WriteLine($"Recording into {options.OutputDirectory}");
Console.WriteLine($"Request spacing {options.Spacing.TotalMilliseconds:F0}ms, max {options.MaxPages} page(s) per list, {(options.Force ? "overwriting" : "skipping")} existing fixtures.");
Directory.CreateDirectory(options.OutputDirectory);

var logger = new ConsoleLogger("recorder");
var writer = new FixtureWriter(options.OutputDirectory, options.Force);

// Pipeline (outermost first): record/reuse → rate-limit gate → network.
//
// The recorder is deliberately ABOVE the throttle, not below it. A request whose fixture already
// exists is answered from disk and never reaches the gate, so a resume runs at full speed instead
// of paying the 2.2s spacing for work that is already done. It also means one fixture per logical
// request rather than one per retry attempt, since the gate's retries happen underneath.
using var network = new HttpClientHandler();
using var rateLimit = new AniListRateLimitHandler(
    TimeProvider.System,
    new ConsoleLogger<AniListRateLimitHandler>("ratelimit"),
    minSpacing: options.Spacing)
{
    InnerHandler = network,
};
using var recording = new RecordingHandler(writer) { InnerHandler = rateLimit };
using var http = new HttpClient(recording) { Timeout = TimeSpan.FromSeconds(60) };

var client = new AniListClient(
    http,
    new StaticTokenAuthService(token),
    new NoOpOutageState(),
    new ConsoleLogger<AniListClient>("anilist"));

try
{
    await new RecordingPlan(client, writer, options, logger).RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Recording stopped: {ex.Message}");
    Console.Error.WriteLine($"{writer.Written} fixture(s) were written before the failure. Re-run to resume — existing files are skipped.");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"Done. {writer.Written} written, {writer.Reused} already present (no request made), {writer.Requests} request(s) issued.");
return 0;

/// <summary>
/// The test account's token: the environment first, then the file <c>driver.ps1 dump-token</c>
/// writes.
/// <para>
/// The file fallback exists because a user-level environment variable set on Windows does not reach
/// processes that are already running — including whatever shell is invoking this — so "I set it"
/// and "this can see it" are not the same thing until a new terminal exists. The file is gitignored
/// and is where dump-token puts the token anyway, so preferring the environment and falling back to
/// it costs nothing and removes a confusing failure.
/// </para>
/// </summary>
static string? ReadToken()
{
    var fromEnvironment = Environment.GetEnvironmentVariable("ANILIST_RECORDER_TOKEN");
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment;
    }

    var path = Path.Combine("tmp", "dev-token.txt");
    return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
}

/// <summary>Command-line options, all with defaults that are safe to run unattended.</summary>
internal sealed class RecorderOptions
{
    public string OutputDirectory { get; private init; } = string.Empty;
    public bool Force { get; private init; }

    /// <summary>
    /// Minimum gap between requests. AniList's documented budget is 90/min but it has been running
    /// degraded at 30/min for a long while, so the default here targets roughly 27/min and leaves
    /// headroom. The handler below it also honours Retry-After, so this is a floor, not the only
    /// protection.
    /// </summary>
    public TimeSpan Spacing { get; private init; }

    /// <summary>How many pages deep to walk any paged list. Keeps a large library from turning into
    /// thousands of requests; page 2 is what proves paging works, so the default is 3.</summary>
    public int MaxPages { get; private init; }

    /// <summary>How many media get the full sort-and-paging treatment. The rest record page 1 only.</summary>
    public int DeepMediaCount { get; private init; }

    /// <summary>
    /// How many list entries get media fixtures at all; 0 means every one.
    /// <para>
    /// This is what decouples the size of the fixture set from the size of the test account. The
    /// account can mirror a real library — useful when signing in on the emulator by hand — while CI
    /// carries a bounded subset, because details are one fixture per title and that is the axis that
    /// actually costs repository and APK weight.
    /// </para>
    /// <para>
    /// The recorded list is trimmed to match (see <c>TrimListFixtures</c>): a Library showing titles
    /// whose details were never recorded is a screen where tapping the wrong row fails the build.
    /// </para>
    /// </summary>
    public int MaxMedia { get; private init; }

    public static RecorderOptions Parse(string[] args)
    {
        var outputDirectory = Path.Combine("src", "AniSprinkles", "Fixtures", "AniList");
        var force = false;
        var spacingMs = 2200;
        var maxPages = 3;
        var deepMedia = 3;
        var maxMedia = 0;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    outputDirectory = args[++i];
                    break;
                case "--force":
                    force = true;
                    break;
                case "--spacing-ms" when i + 1 < args.Length:
                    spacingMs = int.Parse(args[++i]);
                    break;
                case "--max-pages" when i + 1 < args.Length:
                    maxPages = int.Parse(args[++i]);
                    break;
                case "--deep-media" when i + 1 < args.Length:
                    deepMedia = int.Parse(args[++i]);
                    break;
                case "--max-media" when i + 1 < args.Length:
                    maxMedia = int.Parse(args[++i]);
                    break;
                default:
                    throw new ArgumentException($"Unrecognised argument '{args[i]}'.");
            }
        }

        return new RecorderOptions
        {
            OutputDirectory = Path.GetFullPath(outputDirectory),
            Force = force,
            Spacing = TimeSpan.FromMilliseconds(spacingMs),
            MaxPages = maxPages,
            DeepMediaCount = deepMedia,
            MaxMedia = maxMedia,
        };
    }
}

/// <summary>
/// Writes fixtures to disk, one file per request, indented so a re-record reviews as a readable
/// diff rather than a wall of minified JSON.
/// </summary>
internal sealed class FixtureWriter(string outputDirectory, bool force)
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public int Written { get; private set; }

    public int Reused { get; private set; }

    public int Requests { get; private set; }

    /// <summary>
    /// The already-recorded response for <paramref name="key"/>, or null if it must be fetched.
    /// <para>
    /// This is what makes a resume cheap. Answering from disk before the request goes out means a
    /// run interrupted by a 429 — or by anything else — costs only the fixtures that had not landed
    /// yet, instead of spending the whole rate-limit budget again re-fetching what is already on
    /// disk and then discarding it.
    /// </para>
    /// </summary>
    public string? TryReuse(string key)
    {
        if (force)
        {
            return null;
        }

        var path = Path.Combine(outputDirectory, $"{key}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var existing = JsonSerializer.Deserialize<GraphQlFixture>(File.ReadAllText(path), WriteOptions);
        if (existing?.Response is null)
        {
            return null;
        }

        Reused++;
        return existing.Response.ToJsonString();
    }

    public void Write(GraphQlFixture fixture)
    {
        Requests++;
        File.WriteAllText(
            Path.Combine(outputDirectory, $"{fixture.FileName}.json"),
            JsonSerializer.Serialize(fixture, WriteOptions),
            Encoding.UTF8);
        Written++;
        Console.WriteLine($"  + {fixture.FileName}");
    }

    /// <summary>
    /// Cuts the recorded list down to the media that actually got recorded.
    /// <para>
    /// The list arrives as one response containing every entry, while details are one response per
    /// title — so a capped run leaves a Library listing hundreds of titles of which only a handful
    /// can be opened. In CI that is not a cosmetic mismatch: tapping an unrecorded row is a fixture
    /// miss, which fails the build. Trimming keeps the two halves describing the same library.
    /// </para>
    /// <para>
    /// This is the one place a recording is edited rather than replayed verbatim, which is why it is
    /// a deliberate, named step and not a quiet filter inside the replay handler.
    /// </para>
    /// </summary>
    public void TrimListFixtures(IReadOnlySet<int> keptMediaIds)
    {
        foreach (var path in Directory.GetFiles(outputDirectory, "MediaListCollection__*.json"))
        {
            var fixture = JsonSerializer.Deserialize<GraphQlFixture>(File.ReadAllText(path), WriteOptions);
            if (fixture?.Response?["data"]?["MediaListCollection"]?["lists"] is not JsonArray lists)
            {
                continue;
            }

            var removed = 0;
            foreach (var list in lists)
            {
                if (list?["entries"] is not JsonArray entries)
                {
                    continue;
                }

                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var mediaId = entries[i]?["mediaId"]?.GetValue<int>() ?? 0;
                    if (!keptMediaIds.Contains(mediaId))
                    {
                        entries.RemoveAt(i);
                        removed++;
                    }
                }
            }

            if (removed == 0)
            {
                continue;
            }

            File.WriteAllText(path, JsonSerializer.Serialize(fixture, WriteOptions), Encoding.UTF8);
            Console.WriteLine($"  ~ trimmed {removed} unrecorded entrie(s) from {Path.GetFileNameWithoutExtension(path)}");
        }
    }
}

/// <summary>
/// Captures every exchange at the transport layer.
/// <para>
/// Recording here rather than around <see cref="IAniListClient"/> is what makes the fixtures
/// replayable against the real client: what lands on disk is the response envelope exactly as
/// AniList sent it, so nothing in the mapping layer gets baked in as truth.
/// </para>
/// </summary>
internal sealed class RecordingHandler(FixtureWriter writer) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var parsedRequest = requestBody is null ? null : JsonNode.Parse(requestBody)?.AsObject();
        var operationName = parsedRequest?["operationName"]?.GetValue<string>();
        var query = parsedRequest?["query"]?.GetValue<string>();

        if (operationName is null || query is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var variables = parsedRequest?["variables"]?.DeepClone();
        var fingerprint = GraphQlFixtureKey.QueryFingerprint(query);

        if (writer.TryReuse(GraphQlFixtureKey.FileName(operationName, variables, fingerprint)) is { } recorded)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(recorded, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Let the pipeline above deal with it — a 429 is retried by the rate-limit handler and
            // will come back through here on the next attempt. Recording a failure body would put a
            // fixture on disk that answers every future replay with an error.
            return response;
        }

        // Buffer so the body can be read here and again by AniListClient. Without this the client
        // above would be handed a stream this method had already drained.
        await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var responseBody18PlusSafe = JsonNode.Parse(responseBody);
        AdultCoverScrubber.Scrub(operationName, variables, responseBody18PlusSafe);

        writer.Write(new GraphQlFixture
        {
            OperationName = operationName,
            Variables = variables,
            QueryFingerprint = fingerprint,
            RecordedAt = DateTimeOffset.UtcNow,
            Response = responseBody18PlusSafe,
        });

        return response;
    }
}

/// <summary>
/// Strips the artwork off 18+ results before they are written to disk.
/// <para>
/// These fixtures are committed to a public repository, so the metadata is kept — the adult sections
/// still render populated cards with real titles in CI, which is the point of recording them — but
/// no 18+ cover or banner URL is stored, and so none is ever fetched or screenshotted.
/// </para>
/// <para>
/// The replacement deliberately ends in <c>default.jpg</c>, which <c>ImageUrl.IsReal</c> already
/// treats as "no image": the app renders its own placeholder and makes no network request at all.
/// That keeps CI screenshots byte-stable — a live placeholder service would have to be reachable and
/// would risk a different image on every run — and exercises the missing-image path while it is
/// there. The rest of the URL is a joke so that nobody reading a fixture diff mistakes it for a
/// recording that failed.
/// </para>
/// </summary>
internal static class AdultCoverScrubber
{
    private const string SillyPlaceholder =
        "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/a-very-polite-cat-in-a-tiny-hat-default.jpg";

    private static readonly string[] ImageFields = ["medium", "large", "extraLarge", "bannerImage"];

    /// <summary>
    /// The Discover query asks for SFW and 18+ rows in one request, so the request's own
    /// <c>isAdult</c> variable says nothing about these two aliases. They are the only 18+ subtrees
    /// in an otherwise SFW response and have to be scrubbed by name.
    /// </summary>
    private static readonly string[] AdultDiscoverAliases = ["adult", "topRatedAdult"];

    public static void Scrub(string operationName, JsonNode? variables, JsonNode? response)
    {
        if (response is null)
        {
            return;
        }

        // First and most important: anything that declares itself 18+, wherever it appears.
        //
        // The request-level rules below are about *which results* a query returns; they say nothing
        // about a response that mixes ratings. MediaListCollection is exactly that — it takes no
        // isAdult argument at all, so an 18+ title sitting on the account's own list sailed straight
        // past a request-keyed scrub with its cover URL intact and into a public repository. Keying
        // on the media's own flag is the check that cannot be sidestepped by the shape of the query.
        ScrubSelfDeclaredAdult(response);

        if (string.Equals(operationName, "DiscoverSections", StringComparison.Ordinal))
        {
            var data = response["data"];
            foreach (var alias in AdultDiscoverAliases)
            {
                ScrubTree(data?[alias]);
            }

            return;
        }

        // Browse and search filter server-side and do not return isAdult on results at all, so the
        // request's own argument is the only signal available for them.
        if (variables?["isAdult"] is JsonValue isAdult
            && isAdult.TryGetValue<bool>(out var adultOnly)
            && adultOnly)
        {
            ScrubTree(response);
        }
    }

    /// <summary>
    /// Strips artwork from every object carrying <c>isAdult: true</c>, at any depth.
    /// <para>
    /// Deliberately scrubs the object itself rather than the subtree: the flag sits on a media, and a
    /// media's images are its own properties. Recursing into the whole subtree would also blank the
    /// covers of anything nested underneath it, such as a relation to a perfectly ordinary title.
    /// </para>
    /// </summary>
    private static void ScrubSelfDeclaredAdult(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                if (obj["isAdult"] is JsonValue flag
                    && flag.TryGetValue<bool>(out var isAdult)
                    && isAdult)
                {
                    ScrubImagesOn(obj);
                }

                foreach (var property in obj.ToList())
                {
                    ScrubSelfDeclaredAdult(property.Value);
                }

                break;
            }

            case JsonArray array:
                foreach (var item in array)
                {
                    ScrubSelfDeclaredAdult(item);
                }

                break;
        }
    }

    /// <summary>Replaces this object's own image URLs, without descending.</summary>
    private static void ScrubImagesOn(JsonObject media)
    {
        if (media["bannerImage"] is JsonValue)
        {
            media["bannerImage"] = SillyPlaceholder;
        }

        if (media["coverImage"] is JsonObject cover)
        {
            foreach (var property in cover.ToList())
            {
                if (property.Value is JsonValue && property.Key is "medium" or "large" or "extraLarge")
                {
                    cover[property.Key] = SillyPlaceholder;
                }
            }
        }
    }

    private static void ScrubTree(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (ImageFields.Contains(property.Key) && property.Value is JsonValue)
                    {
                        obj[property.Key] = SillyPlaceholder;
                        continue;
                    }

                    ScrubTree(property.Value);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    ScrubTree(item);
                }

                break;
        }
    }
}

/// <summary>
/// What gets recorded, in order. The plan is self-directing: it records the viewer's list first and
/// then follows the ids it finds — media, then their cast, staff, studios and recommendations — so
/// nothing here hard-codes an AniList id that could rot. Curate the test account's list and the plan
/// follows it.
/// </summary>
internal sealed class RecordingPlan(
    AniListClient client,
    FixtureWriter writer,
    RecorderOptions options,
    ConsoleLogger logger)
{
    // These mirror the SortOption lists on the four details page models. They are duplicated rather
    // than shared because those lists are instance properties on DI-constructed page models, which a
    // standalone tool cannot reach.
    //
    // Drift is caught rather than prevented: replay fails loudly on a missing fixture, so adding a
    // sort to a page model without re-recording breaks CI with "no fixture for this request" instead
    // of quietly rendering an empty section — which is the exact failure mode (#134) this removes.
    private static readonly string[] MediaCharacterSorts = ["ROLE", "FAVOURITES_DESC", "RELEVANCE"];
    private static readonly string[] MediaStaffSorts = ["RELEVANCE", "FAVOURITES_DESC", "ROLE"];
    private static readonly string[] MediaRecommendationSorts = ["RATING_DESC"];
    private static readonly string[] StaffVoiceRoleSorts = ["FAVOURITES_DESC", "ROLE"];
    private static readonly string[] MediaListSorts =
        ["POPULARITY_DESC", "SCORE_DESC", "FAVOURITES_DESC", "START_DATE_DESC", "START_DATE", "TITLE_ROMAJI"];

    // CI types "no" into Search and waits for a result, so that query must have a fixture or the
    // capture times out. The others are here so the app stays explorable by hand in a CI build.
    private static readonly string[] SearchTerms = ["no", "one", "ka"];

    public async Task RunAsync()
    {
        logger.Step("Viewer");
        var viewer = await client.GetViewerAsync();
        Console.WriteLine($"  recording as: {viewer.Name} (id {viewer.Id})");
        await client.GetCurrentUserIdAsync();

        logger.Step("Library");
        var animeGroups = await client.GetMediaListGroupedAsync(MediaKind.Anime);
        var mangaGroups = await client.GetMediaListGroupedAsync(MediaKind.Manga);

        var animeIds = SelectMediaIds(animeGroups, options.MaxMedia);
        var mangaIds = SelectMediaIds(mangaGroups, options.MaxMedia);
        Console.WriteLine($"  {animeIds.Count} anime, {mangaIds.Count} manga selected for recording");

        if (animeIds.Count == 0)
        {
            throw new InvalidOperationException(
                "The account's anime list is empty. Curate the test account before recording — the plan "
                + "follows the list, so an empty list records almost nothing.");
        }

        await RecordDiscoverAsync();
        await RecordBrowseAsync();
        await RecordSearchAsync();
        await RecordAiringAsync(animeIds);
        await RecordMediaAsync(animeIds, mangaIds);

        // Last, because it needs to know what actually got recorded. The account can hold a whole
        // real library while CI carries a bounded slice of it; this is what stops the recorded
        // Library from advertising titles whose details were never captured.
        writer.TrimListFixtures(new HashSet<int>(animeIds.Concat(mangaIds)));
    }

    private async Task RecordDiscoverAsync()
    {
        logger.Step("Discover sections");
        var now = DateTimeOffset.UtcNow;
        var (season, seasonYear) = AniListSeason.Current(now);
        var (nextSeason, nextSeasonYear) = AniListSeason.Next(now);

        // Both axes, because they select different queries and different filters: filterAdult drives
        // whether isAdult:false is sent at all, and includeAdultSections swaps in the variant with
        // the two 18+ aliases. The canary contract depends on the SFW combination staying clean.
        foreach (var filterAdult in new[] { true, false })
        {
            foreach (var includeAdultSections in new[] { false, true })
            {
                await client.GetDiscoverSectionsAsync(
                    season, seasonYear, nextSeason, nextSeasonYear, filterAdult, includeAdultSections);
            }
        }
    }

    private async Task RecordBrowseAsync()
    {
        logger.Step("Browse (View All)");
        var now = DateTimeOffset.UtcNow;
        var (season, seasonYear) = AniListSeason.Current(now);
        var (nextSeason, nextSeasonYear) = AniListSeason.Next(now);

        foreach (var definition in DiscoverSectionDefinitions.All)
        {
            var (definitionSeason, definitionYear) = definition.SeasonKind switch
            {
                DiscoverSeasonKind.Current => (season, (int?)seasonYear),
                DiscoverSeasonKind.Next => (nextSeason, (int?)nextSeasonYear),
                _ => (null, null),
            };

            // null and false are both real: null omits the filter (adult toggle on), false pins SFW.
            foreach (var isAdult in new bool?[] { false, null })
            {
                var effectiveIsAdult = definition.AdultFilter ?? isAdult;
                await WalkPagesAsync(page => client.BrowseAnimePageAsync(
                    definition.Sort, definition.Status, definitionSeason, definitionYear,
                    effectiveIsAdult, definition.Format, page));

                if (definition.AdultFilter is not null)
                {
                    // The 18+ sections pin their own filter, so the outer loop would otherwise record
                    // the identical request twice.
                    break;
                }
            }
        }
    }

    private async Task RecordSearchAsync()
    {
        logger.Step("Search");
        foreach (var term in SearchTerms)
        {
            foreach (MediaKind? kind in new MediaKind?[] { null, MediaKind.Anime, MediaKind.Manga })
            {
                foreach (var isAdult in new bool?[] { false, true, null })
                {
                    await WalkPagesAsync(page => client.SearchMediaPageAsync(term, kind, isAdult, page));
                }
            }
        }
    }

    private async Task RecordAiringAsync(IReadOnlyList<int> animeIds)
    {
        logger.Step("Airing schedule");
        var now = DateTimeOffset.UtcNow;
        await client.GetAiringScheduleAsync(
            animeIds,
            (int)now.AddDays(-7).ToUnixTimeSeconds(),
            (int)now.AddDays(7).ToUnixTimeSeconds());
    }

    private async Task RecordMediaAsync(IReadOnlyList<int> animeIds, IReadOnlyList<int> mangaIds)
    {
        var characterIds = new HashSet<int>();
        var staffIds = new HashSet<int>();
        var studioIds = new HashSet<int>();
        var recommendedIds = new HashSet<int>();

        var deep = 0;
        foreach (var id in animeIds.Concat(mangaIds))
        {
            logger.Step($"Media {id}");

            Media? media;
            try
            {
                (media, _) = await client.GetMediaAsync(id);
            }
            catch (AniListApiException ex)
            {
                // A list can outlive the media on it: AniList answers Not Found for ids that are
                // still perfectly happy sitting in MediaListCollection. Losing a whole recording run
                // to one of them would be absurd — and worse, the run is long enough that the
                // failure lands minutes after the cause, so skip loudly and carry on.
                logger.Skip($"media {id}: {ex.Kind} — {ex.Message}");
                continue;
            }

            if (media is null)
            {
                continue;
            }

            foreach (var edge in media.Characters)
            {
                if (edge.Node is { Id: > 0 } node)
                {
                    characterIds.Add(node.Id);
                }

                if (edge.VoiceActors.FirstOrDefault() is { Id: > 0 } voiceActor)
                {
                    staffIds.Add(voiceActor.Id);
                }
            }

            foreach (var edge in media.Staff)
            {
                if (edge.Node is { Id: > 0 } node)
                {
                    staffIds.Add(node.Id);
                }
            }

            foreach (var studio in media.Studios.Where(s => s.Id > 0))
            {
                studioIds.Add(studio.Id);
            }

            foreach (var recommendation in media.Recommendations)
            {
                if (recommendation.MediaRecommendation is { Id: > 0 } recommended)
                {
                    recommendedIds.Add(recommended.Id);
                }
            }

            // Every media records page 1 of each section sort, which is what a sort change asks for.
            // Only the first few walk deeper — Load More is proven by page 2 existing somewhere, not
            // by every title in the library carrying three pages of cast.
            var pages = deep < options.DeepMediaCount ? options.MaxPages : 1;
            deep++;

            await SafelyAsync($"media {id} sections", async () =>
            {
                foreach (var sort in MediaCharacterSorts)
                {
                    await WalkPagesAsync(page => client.LoadMediaCharactersPageAsync(id, page, sort), pages);
                }

                foreach (var sort in MediaStaffSorts)
                {
                    await WalkPagesAsync(page => client.LoadMediaStaffPageAsync(id, page, sort), pages);
                }

                foreach (var sort in MediaRecommendationSorts)
                {
                    await WalkPagesAsync(page => client.LoadMediaRecommendationsPageAsync(id, page, sort), pages);
                }
            });
        }

        // Recommendations reached from a details page are off-list, so they exercise the
        // "no list entry" branch that an on-list media never reaches.
        foreach (var id in recommendedIds.Except(animeIds).Except(mangaIds).Take(options.DeepMediaCount * 2))
        {
            logger.Step($"Off-list media {id}");
            await client.GetMediaAsync(id);
        }

        await RecordPeopleAsync(characterIds, staffIds, studioIds);
    }

    private async Task RecordPeopleAsync(
        IReadOnlySet<int> characterIds, IReadOnlySet<int> staffIds, IReadOnlySet<int> studioIds)
    {
        foreach (var id in characterIds.Take(options.DeepMediaCount))
        {
            logger.Step($"Character {id}");
            await SafelyAsync($"character {id}", async () =>
            {
                foreach (var sort in MediaListSorts)
                {
                    await client.GetCharacterAsync(id, sort);
                    await WalkPagesAsync(page => client.LoadCharacterMediaPageAsync(id, page, sort));
                }
            });
        }

        foreach (var id in staffIds.Take(options.DeepMediaCount))
        {
            logger.Step($"Staff {id}");
            await SafelyAsync($"staff {id}", async () =>
            {
                foreach (var charactersSort in StaffVoiceRoleSorts)
                {
                    await client.GetStaffAsync(id, charactersSort);
                    await WalkPagesAsync(page => client.LoadStaffCharactersPageAsync(id, page, charactersSort));
                }

                foreach (var mediaSort in MediaListSorts)
                {
                    await WalkPagesAsync(page => client.LoadStaffMediaPageAsync(id, page, mediaSort));
                }
            });
        }

        foreach (var id in studioIds.Take(options.DeepMediaCount))
        {
            logger.Step($"Studio {id}");
            await SafelyAsync($"studio {id}", async () =>
            {
                foreach (var sort in MediaListSorts)
                {
                    await client.GetStudioAsync(id, sort);
                    await WalkPagesAsync(page => client.LoadStudioMediaPageAsync(id, page, sort));
                }
            });
        }
    }

    /// <summary>
    /// Runs one unit of recording, letting an AniList failure cost that unit rather than the run.
    /// <para>
    /// Deliberately narrow: it catches <see cref="AniListApiException"/> only, so a bug in this tool
    /// still surfaces as a crash. Everything above resumes cheaply — recorded fixtures are answered
    /// from disk — but a run that dies twenty minutes in over one bad id is still twenty minutes of
    /// wall clock nobody gets back.
    /// </para>
    /// </summary>
    private async Task SafelyAsync(string what, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (AniListApiException ex)
        {
            logger.Skip($"{what}: {ex.Kind} — {ex.Message}");
        }
    }

    /// <summary>
    /// Walks a paged endpoint until AniList says there is no next page or the cap is reached.
    /// Recording page 2 is the whole point: it is the page an empty-list stub could never produce.
    /// </summary>
    private async Task WalkPagesAsync<T>(
        Func<int, Task<(IReadOnlyList<T> Items, PageInfo? PageInfo)>> fetch, int? maxPages = null)
    {
        var cap = maxPages ?? options.MaxPages;
        for (var page = 1; page <= cap; page++)
        {
            var (_, pageInfo) = await fetch(page);
            if (pageInfo?.HasNextPage != true)
            {
                return;
            }
        }
    }

    private static int MediaIdOf(MediaListEntry entry)
        => entry.MediaId > 0 ? entry.MediaId : entry.Media?.Id ?? 0;

    /// <summary>
    /// The media to record, capped at <c>--max-media</c> and spread across the status groups.
    /// <para>
    /// Taking the first N off the flattened list would land entirely inside the first group or two —
    /// on a real library that means every recorded title is Watching or Planning, and the Library's
    /// Completed, Dropped and Paused sections have no details behind them at all. Round-robin costs
    /// depth within each status instead of costing whole statuses.
    /// </para>
    /// </summary>
    private static List<int> SelectMediaIds(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups, int max)
    {
        var seen = new HashSet<int>();
        var all = new List<int>();

        foreach (var id in groups.SelectMany(g => g.Entries).Select(MediaIdOf).Where(id => id > 0))
        {
            if (seen.Add(id))
            {
                all.Add(id);
            }
        }

        if (max <= 0 || all.Count <= max)
        {
            return all;
        }

        var queues = groups
            .Select(g => new Queue<int>(g.Entries.Select(MediaIdOf).Where(id => id > 0)))
            .ToList();

        var taken = new List<int>(max);
        var takenSet = new HashSet<int>();

        while (taken.Count < max && queues.Exists(q => q.Count > 0))
        {
            foreach (var queue in queues)
            {
                if (taken.Count >= max)
                {
                    break;
                }

                // Inner loop because an id can appear in more than one custom list; skip the
                // duplicate and take this group's next unseen entry rather than losing its turn.
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    if (takenSet.Add(id))
                    {
                        taken.Add(id);
                        break;
                    }
                }
            }
        }

        return taken;
    }
}

/// <summary>Supplies the recorder's token. The app's own AuthService is unreachable off-device.</summary>
internal sealed class StaticTokenAuthService(string token) : IAuthService
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(token);

    public Task<bool> SignInAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task SignOutAsync() => Task.CompletedTask;
}

/// <summary>Outage tracking is a UI concern; the recorder just lets exceptions surface.</summary>
internal sealed class NoOpOutageState : IOutageStateService
{
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

    public bool IsOutage => false;

    public string Title => string.Empty;

    public string Subtitle => string.Empty;

    public string IconGlyph => string.Empty;

    public void ReportFailure(Exception ex)
    {
    }

    public void ReportSuccess()
    {
    }
}

/// <summary>Console logging without pulling in a logging-provider package.</summary>
internal class ConsoleLogger(string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        Console.Error.WriteLine($"  [{category}] {logLevel}: {formatter(state, exception)}");
    }

    public void Step(string name) => Console.WriteLine(name);

    /// <summary>
    /// Something was skipped rather than recorded. Stands out because the run is long and unattended:
    /// a skip buried in hundreds of "+ fixture" lines is a hole in the fixture set that nobody
    /// notices until CI cannot find a recording.
    /// </summary>
    public void Skip(string what) => Console.WriteLine($"  ~ SKIPPED {what}");
}

internal sealed class ConsoleLogger<T>(string category) : ConsoleLogger(category), ILogger<T>;
