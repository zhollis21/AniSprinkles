// Seeds the CI test account's anime and manga lists from a public AniList profile, so the fixture
// recorder (tools/record-anilist-fixtures.cs) has a realistic library to follow.
//
// The source read needs no credential. AniList lists are public by default and MediaListCollection
// accepts a userName without auth, which has a useful consequence: private entries are never
// returned in the first place, so nothing here has to remember to filter them out. Notes are dropped
// explicitly — they are free text, they are personal, and everything this seeds ends up recorded
// into fixtures that live in a public repository.
//
// Only the destination is written to, and only with the test account's own token.
//
// Usage (from the repo root):
//   $env:ANILIST_RECORDER_TOKEN = "<token for the test account>"
//   dotnet run tools/clone-anilist-list.cs -- --from <your-anilist-username> --dry-run
//   dotnet run tools/clone-anilist-list.cs -- --from <your-anilist-username>
//
// --dry-run prints exactly what would be written and touches nothing. Run it first: this writes to
// a real AniList account, and the projected recorder cost it prints is worth seeing before you
// commit to a library size.
//
// Re-running is safe. SaveMediaListEntry upserts on mediaId, so a second run overwrites the same
// entries rather than duplicating them.

// The file-based-app default turns on the trim/AOT analysers, which flag the reflection-based
// JsonSerializer calls below. This tool is run by hand from the repo root and is never published,
// let alone AOT-compiled, so the analysis has nothing to say here.
#:property PublishAot=false
#:property IsAotCompatible=false
#:project ../src/AniSprinkles.Core/AniSprinkles.Core.csproj

using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AniSprinkles.Models;
using AniSprinkles.Services;
using AniSprinkles.Services.Abstractions;
using Microsoft.Extensions.Logging;

var options = CloneOptions.Parse(args);
if (options is null)
{
    return 1;
}

// Empty rather than null on the dry-run path: nothing authenticates there, and carrying a nullable
// through to the client construction below would only be resolved by a suppression.
var targetToken = ReadToken() ?? string.Empty;
if (!options.DryRun && string.IsNullOrWhiteSpace(targetToken))
{
    Console.Error.WriteLine("""
        No token found — neither ANILIST_RECORDER_TOKEN nor tmp/dev-token.txt.

        This is the token for the CI *test* account — the same one tools/record-anilist-fixtures.cs
        uses, so that what you seed is what you later record. Reading the source profile needs no
        token at all.

        The easy way is to let the app do the OAuth flow: sign in on the emulator as the test
        account, then

            pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 dump-token

        Or set it yourself:

            $env:ANILIST_RECORDER_TOKEN = "<token>"      # PowerShell
            export ANILIST_RECORDER_TOKEN="<token>"      # bash

        Or pass --dry-run to see what would be written without needing it.
        """);
    return 1;
}

using var network = new HttpClientHandler();
using var rateLimit = new AniListRateLimitHandler(
    TimeProvider.System,
    new ConsoleLogger<AniListRateLimitHandler>("ratelimit"),
    minSpacing: options.Spacing)
{
    InnerHandler = network,
};
// BaseAddress is set here rather than left to AniListClient's constructor, which assigns it only
// when null. By the time the target client is built, PublicAniListReader has already sent the source
// reads through this same HttpClient — and HttpClient refuses property changes once a request has
// gone out, so the constructor would throw instead of writing anything.
using var http = new HttpClient(rateLimit)
{
    Timeout = TimeSpan.FromSeconds(60),
    BaseAddress = new Uri("https://graphql.anilist.co"),
};

var source = new PublicAniListReader(http);

SourceUser? sourceUser = null;
var selected = new List<MediaListEntry>();

if (!string.IsNullOrWhiteSpace(options.SourceUserName))
{
    Console.WriteLine($"Reading public profile: {options.SourceUserName}");

    try
    {
        sourceUser = await source.GetUserAsync(options.SourceUserName);
    }
    catch (Exception ex)
    {
        // AniList disables its API outright during incidents (HTTP 403 with an explanatory body)
        // often enough that the app carries a whole outage-banner subsystem for it. A stack trace
        // here would read as a bug in this tool rather than as "the service is down, try later".
        Console.Error.WriteLine();
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine("Nothing was written. Re-run when AniList is reachable again.");
        return 1;
    }

    if (sourceUser is null)
    {
        Console.Error.WriteLine(
            $"No public AniList profile found for '{options.SourceUserName}'. Check the spelling, and "
            + "that the profile and its lists are not set to private.");
        return 1;
    }

    Console.WriteLine($"  {sourceUser.Name} (id {sourceUser.Id}), score format {sourceUser.ScoreFormat}");

    var anime = await source.GetListAsync(options.SourceUserName, MediaKind.Anime);
    var manga = await source.GetListAsync(options.SourceUserName, MediaKind.Manga);

    selected.AddRange(Select(anime, options.Limit));
    selected.AddRange(Select(manga, options.Limit));

    if (selected.Count == 0 && options.Additions.Count == 0)
    {
        Console.Error.WriteLine(
            "The source profile returned no list entries. If the lists are public and non-empty, the "
            + "profile may have list visibility restricted.");
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine($"{anime.Count} anime and {manga.Count} manga entries found; {selected.Count} selected.");
}

selected.AddRange(options.Additions);

if (options.Additions.Count > 0)
{
    Console.WriteLine($"{options.Additions.Count} entrie(s) named explicitly with --add.");
}

Report(selected);

// Recording cost scales with how many media get recorded, and it is the part that takes real time.
// Saying so here is cheaper than discovering it 40 minutes into a recording run.
//
// Note this is the cost of recording *everything* seeded. Seeding a whole library on purpose is a
// reasonable thing to do — it makes the account realistic to drive by hand — and the recorder's
// --max-media is what keeps the fixture set bounded regardless of how large the account is.
Console.WriteLine();
Console.WriteLine($"Projected recorder cost if every entry is recorded: roughly {selected.Count * 8} requests, ~{selected.Count * 8 * 2.2 / 60:F0} minutes at the default spacing.");
Console.WriteLine("Bound it with `--max-media N` when recording (the recorded list is trimmed to match), or --deep-media 1.");

if (options.DryRun)
{
    Console.WriteLine();
    Console.WriteLine("--dry-run: nothing was written.");
    return 0;
}

var target = new AniListClient(
    http,
    new StaticTokenAuthService(targetToken),
    new NoOpOutageState(),
    new ConsoleLogger<AniListClient>("anilist"));

var targetUser = await target.GetViewerAsync();
Console.WriteLine();
Console.WriteLine($"Writing to: {targetUser.Name} (id {targetUser.Id})");

// Both guards below need a source profile, and there isn't one on an --add-only run.
if (sourceUser is not null)
{
    if (targetUser.Id == sourceUser.Id)
    {
        Console.Error.WriteLine(
            "Source and destination are the same account. That would rewrite your real list — refusing.");
        return 1;
    }

    // Scores come back in the *source* account's format, and SaveMediaListEntry interprets them in
    // the *destination* account's. Left mismatched, a 9.5 out of 10 would be stored as 9.5 out of 100.
    // --add scores are written in the destination's format too, so this alignment running first is
    // what makes it safe to pass both in one invocation.
    if (targetUser.ScoreFormat != sourceUser.ScoreFormat)
    {
        Console.WriteLine($"  aligning score format {targetUser.ScoreFormat} -> {sourceUser.ScoreFormat}");
        await target.UpdateUserAsync(new UpdateUserRequest { ScoreFormat = sourceUser.ScoreFormat });
    }
}
else
{
    Console.WriteLine($"  destination score format is {targetUser.ScoreFormat} — --add scores are read in that scale");
}

var written = 0;
var failed = 0;
foreach (var entry in selected)
{
    try
    {
        await target.SaveMediaListEntryAsync(entry);
        written++;
        Console.WriteLine($"  + {entry.MediaId} {entry.Status} {(entry.Score is > 0 ? entry.Score.ToString() : "-")}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"  ! {entry.MediaId} failed: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"Done. {written} entrie(s) written, {failed} failed.");
Console.WriteLine("Next: dotnet run tools/record-anilist-fixtures.cs");
return failed == 0 ? 0 : 1;

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

/// <summary>
/// Takes at most <paramref name="limit"/> entries, spread across statuses rather than taken off the
/// front.
/// <para>
/// AniList returns the lists grouped, so a plain Take walks them in order and a small limit lands
/// entirely inside the first group or two. On a real library that means every seeded entry is
/// Watching or Planning and the Library's Completed section — plus Dropped, Paused and Rewatching —
/// simply does not exist in the fixtures. Round-robin instead, so a limit costs depth within each
/// status rather than whole statuses.
/// </para>
/// </summary>
static IEnumerable<MediaListEntry> Select(IReadOnlyList<MediaListEntry> entries, int limit)
{
    if (limit <= 0 || entries.Count <= limit)
    {
        return entries;
    }

    var queues = entries
        .GroupBy(e => e.Status)
        .Select(g => new Queue<MediaListEntry>(g))
        .ToList();

    var taken = new List<MediaListEntry>(limit);
    while (taken.Count < limit && queues.Exists(q => q.Count > 0))
    {
        foreach (var queue in queues)
        {
            if (taken.Count >= limit)
            {
                break;
            }

            if (queue.Count > 0)
            {
                taken.Add(queue.Dequeue());
            }
        }
    }

    return taken;
}

static void Report(IReadOnlyList<MediaListEntry> entries)
{
    foreach (var group in entries.GroupBy(e => e.Status).OrderBy(g => g.Key))
    {
        Console.WriteLine($"  {group.Key,-10} {group.Count()}");
    }
}

/// <summary>Command-line options. Returns null (after printing why) when the arguments are unusable.</summary>
internal sealed class CloneOptions
{
    public string SourceUserName { get; private init; } = string.Empty;

    /// <summary>Entries to take per media type; 0 means every entry the profile exposes.</summary>
    public int Limit { get; private init; }

    public bool DryRun { get; private init; }

    public TimeSpan Spacing { get; private init; }

    /// <summary>
    /// Entries named outright rather than copied from a profile, as
    /// <c>--add &lt;mediaId&gt;:&lt;STATUS&gt;[:&lt;progress&gt;]</c>.
    /// <para>
    /// The clone follows a real library, which is the point — but some shapes worth covering are not
    /// in anybody's list in the right state. The manga edge cases the CI stubs currently fake by hand
    /// (a one-shot, a novel, an ongoing series with no chapter count) are the reason this exists.
    /// </para>
    /// </summary>
    public IReadOnlyList<MediaListEntry> Additions { get; private init; } = [];

    public static CloneOptions? Parse(string[] args)
    {
        string? from = null;
        var limit = 0;
        var dryRun = false;
        var spacingMs = 2200;
        var additions = new List<MediaListEntry>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--add" when i + 1 < args.Length:
                {
                    var parsed = ParseAddition(args[++i]);
                    if (parsed is null)
                    {
                        return null;
                    }

                    additions.Add(parsed);
                    break;
                }

                case "--from" when i + 1 < args.Length:
                    from = args[++i];
                    break;
                case "--limit" when i + 1 < args.Length:
                    limit = int.Parse(args[++i]);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--spacing-ms" when i + 1 < args.Length:
                    spacingMs = int.Parse(args[++i]);
                    break;
                default:
                    Console.Error.WriteLine($"Unrecognised argument '{args[i]}'.");
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(from) && additions.Count == 0)
        {
            Console.Error.WriteLine(
                "Nothing to do. Pass --from <anilist-username> to copy a public profile (no token "
                + "needed to read it), or --add <mediaId>:<STATUS>[:<progress>] to name entries "
                + "outright. Both together is fine.");
            return null;
        }

        return new CloneOptions
        {
            SourceUserName = from ?? string.Empty,
            Limit = limit,
            DryRun = dryRun,
            Spacing = TimeSpan.FromMilliseconds(spacingMs),
            Additions = additions,
        };
    }

    /// <summary>
    /// Parses <c>id=53390,status=Current,progress=100,score=9.5,volumes=25,repeat=1</c>.
    /// <para>
    /// Key/value rather than positional colons because the interesting part of a seeded entry is the
    /// spread — scored and unscored, chapter- and volume-tracked, a reread — and a fixture set that
    /// only ever shows one shape cannot catch a regression in the others. Omitted keys stay null,
    /// which is itself a case worth covering: an entry with no score renders differently from one
    /// scored zero.
    /// </para>
    /// <para>
    /// Scores are in the destination account's own format, so pair this with <c>--from</c> when the
    /// formats differ — the alignment runs before any write.
    /// </para>
    /// </summary>
    private static MediaListEntry? ParseAddition(string value)
    {
        var entry = new MediaListEntry();
        var sawId = false;

        foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2)
            {
                Console.Error.WriteLine($"--add wants key=value pairs — got '{pair}'.");
                return null;
            }

            var key = kv[0].Trim().ToLowerInvariant();
            var raw = kv[1].Trim();

            switch (key)
            {
                case "id" when int.TryParse(raw, out var id):
                    entry.MediaId = id;
                    sawId = true;
                    break;
                case "status" when Enum.TryParse<MediaListStatus>(raw, ignoreCase: true, out var status):
                    entry.Status = status;
                    break;
                case "progress" when int.TryParse(raw, out var progress):
                    entry.Progress = progress;
                    break;
                case "volumes" when int.TryParse(raw, out var volumes):
                    entry.ProgressVolumes = volumes;
                    break;
                case "score" when double.TryParse(raw, out var score):
                    entry.Score = score;
                    break;
                case "repeat" when int.TryParse(raw, out var repeat):
                    entry.Repeat = repeat;
                    break;
                default:
                    Console.Error.WriteLine(
                        $"--add: unrecognised or unparseable '{pair}'. Keys are id, status, progress, "
                        + "volumes, score, repeat. Status is one of Current, Planning, Completed, "
                        + "Dropped, Paused, Repeating.");
                    return null;
            }
        }

        if (!sawId)
        {
            Console.Error.WriteLine($"--add needs an id, e.g. --add id=53390,status=Current — got '{value}'.");
            return null;
        }

        return entry;
    }
}

/// <summary>The source half of the clone: unauthenticated reads of a public profile.</summary>
/// <remarks>
/// This does not go through <see cref="AniListClient"/>, which addresses the list by the
/// authenticated viewer's own id. Reading someone else's public list by name is a different query,
/// and issuing it directly is what keeps a real account's credential out of this tool entirely.
/// </remarks>
internal sealed class PublicAniListReader(HttpClient http)
{
    private const string Endpoint = "https://graphql.anilist.co";

    private const string UserQuery = """
        query SourceUser($name: String) {
          User(name: $name) {
            id
            name
            mediaListOptions { scoreFormat }
          }
        }
        """;

    private const string ListQuery = """
        query SourceList($name: String, $type: MediaType) {
          MediaListCollection(userName: $name, type: $type) {
            lists {
              name
              entries {
                mediaId
                status
                progress
                progressVolumes
                score
                repeat
                hiddenFromStatusLists
              }
            }
          }
        }
        """;

    public async Task<SourceUser?> GetUserAsync(string name)
    {
        var data = await PostAsync(UserQuery, new { name });
        var user = data?["User"];
        if (user is null)
        {
            return null;
        }

        // A User with no id is not a profile this can act on — the same-account guard downstream is
        // built on it — so treat it as "not found" rather than asserting it away.
        if (user["id"]?.GetValue<int>() is not { } id)
        {
            return null;
        }

        var scoreFormat = user["mediaListOptions"]?["scoreFormat"]?.GetValue<string>();
        return new SourceUser(
            id,
            user["name"]?.GetValue<string>() ?? name,
            ParseScoreFormat(scoreFormat));
    }

    public async Task<IReadOnlyList<MediaListEntry>> GetListAsync(string name, MediaKind kind)
    {
        var data = await PostAsync(ListQuery, new { name, type = kind.ToAniListType() });
        var lists = data?["MediaListCollection"]?["lists"]?.AsArray();
        if (lists is null)
        {
            return [];
        }

        var results = new List<MediaListEntry>();
        var seen = new HashSet<int>();

        foreach (var list in lists)
        {
            foreach (var entry in list?["entries"]?.AsArray() ?? [])
            {
                if (entry is null)
                {
                    continue;
                }

                var mediaId = entry["mediaId"]?.GetValue<int>() ?? 0;

                // AniList returns an entry once per custom list it belongs to, so the same media can
                // appear several times. Seeding it twice is harmless but noisy, and it would inflate
                // the projected recorder cost printed above.
                if (mediaId <= 0 || !seen.Add(mediaId))
                {
                    continue;
                }

                results.Add(new MediaListEntry
                {
                    MediaId = mediaId,
                    Status = ParseStatus(entry["status"]?.GetValue<string>()),
                    Progress = entry["progress"]?.GetValue<int?>(),
                    ProgressVolumes = entry["progressVolumes"]?.GetValue<int?>(),
                    Score = entry["score"]?.GetValue<double?>(),
                    Repeat = entry["repeat"]?.GetValue<int?>(),
                    HiddenFromStatusLists = entry["hiddenFromStatusLists"]?.GetValue<bool?>(),

                    // Deliberately not copied. Notes are personal free text and everything seeded
                    // here is eventually recorded into fixtures in a public repository.
                    Notes = null,
                });
            }
        }

        return results;
    }

    private async Task<JsonNode?> PostAsync(string query, object variables)
    {
        var payload = JsonSerializer.Serialize(
            new { query, variables },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        // AniList 403s any request carrying neither a Referer nor an Authorization header (#160).
        // This read is deliberately unauthenticated — it is someone else's public profile — so the
        // Referer is the only thing that gets it through. AniListClient sets the same header for
        // everything that goes through it; this path does not, so it sets its own.
        request.Headers.Referrer = new Uri("https://anilist.co/");

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"AniList returned {(int)response.StatusCode} for the public read: {Truncate(body)}");
        }

        var parsed = JsonNode.Parse(body);
        if (parsed?["errors"] is JsonArray { Count: > 0 } errors)
        {
            throw new InvalidOperationException(
                $"AniList rejected the public read: {errors[0]?["message"]?.GetValue<string>()}");
        }

        return parsed?["data"];
    }

    private static string Truncate(string value)
        => value.Length > 300 ? value[..300] + "..." : value;

    private static MediaListStatus? ParseStatus(string? status) => status switch
    {
        "CURRENT" => MediaListStatus.Current,
        "PLANNING" => MediaListStatus.Planning,
        "COMPLETED" => MediaListStatus.Completed,
        "DROPPED" => MediaListStatus.Dropped,
        "PAUSED" => MediaListStatus.Paused,
        "REPEATING" => MediaListStatus.Repeating,
        _ => null,
    };

    private static ScoreFormat ParseScoreFormat(string? format) => format switch
    {
        "POINT_10_DECIMAL" => ScoreFormat.Point10Decimal,
        "POINT_10" => ScoreFormat.Point10,
        "POINT_5" => ScoreFormat.Point5,
        "POINT_3" => ScoreFormat.Point3,
        _ => ScoreFormat.Point100,
    };
}

internal sealed record SourceUser(int Id, string Name, ScoreFormat ScoreFormat);

/// <summary>Supplies the destination account's token. The app's AuthService is unreachable off-device.</summary>
internal sealed class StaticTokenAuthService(string token) : IAuthService
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(token);

    public Task<bool> SignInAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task SignOutAsync() => Task.CompletedTask;
}

/// <summary>Outage tracking is a UI concern; this tool just lets exceptions surface.</summary>
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
internal sealed class ConsoleLogger<T>(string category) : ILogger<T>
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
}
