#if DEBUG
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace AniSprinkles.Services.Fixtures;

/// <summary>
/// The adult-content canary (#118), carried over from <c>CIAniListClient</c> into the replayed
/// fixtures (#134).
/// <para>
/// Before this existed the adult filter was never exercised end to end: <c>DiscoverSectionFetch</c>,
/// the browse/search <c>isAdult</c> argument and <c>MediaListSectionsMerger</c> could all have broken
/// without changing a single screenshot. The canary is a marker, not content — no cover image,
/// nothing to see. The CI workflow dumps the UI after each capture and fails the run if the marker
/// renders anywhere.
/// </para>
/// <para>
/// It has to be synthetic rather than recorded. A recorded 18+ title would put real adult metadata in
/// a public repository, and more importantly the gate needs an item guaranteed present and
/// guaranteed filtered — which no live data can promise.
/// </para>
/// <para>
/// The three parts work differently, deliberately:
/// <list type="bullet">
///   <item><description>
///     <b>The list</b> has no <c>isAdult</c> argument, so the canary always comes back and the
///     client-side filter in <c>MediaListSectionsMerger</c> must drop it. Always armed.
///   </description></item>
///   <item><description>
///     <b>Browse, search and Discover</b> filter server-side, so the canary is served only when the
///     request does <em>not</em> pin <c>isAdult: false</c>. The stub viewer leaves adult content off
///     and nothing in CI turns it on, so a correct app never sees it — and an app that drops or
///     inverts the argument gets it immediately.
///   </description></item>
///   <item><description>
///     <b>The details pages</b> (#199) have no <c>isAdult</c> argument on any nested connection,
///     and AniList returns 18+ titles in them signed in or out, so like the list they are always
///     armed. Each connection is targeted by shape (<see cref="DetailsSplices"/>), and the
///     client-side <c>AdultMediaFilter</c> must drop what is spliced in.
///   </description></item>
/// </list>
/// </para>
/// </summary>
public static class AdultCanary
{
    /// <summary>
    /// The marker the CI workflow greps for, in the UI dump and in this file. Renaming it here
    /// without updating <c>ci-build-and-preview.yml</c> would leave the gate searching for a string
    /// nothing emits — a gate that detects everything and blocks nothing.
    /// </summary>
    public const string Title = "18PLUS CANARY - FILTER FAILED";

    /// <summary>
    /// The prefix every made-up 18+ title carries, the canary included. The workflow's gate greps for
    /// this, so any of them rendering with adult content off fails the build, not just the canary.
    /// </summary>
    public const string Marker = "18PLUS CANARY";

    /// <summary>
    /// The CI build's only adult content.
    /// <para>
    /// The recorder keeps every real Ecchi and 18+ title out of the fixtures
    /// (<see cref="MatureContentFilter"/>), so without these the 18+ Discover rows, adult search and
    /// an adult-on Library would all be empty, with nothing to test against. They are served
    /// wherever the canary is, carry its marker, and have no cover. Their details are answered too
    /// (<see cref="TryAnswer"/>), so tapping one opens a page rather than failing on a fixture miss.
    /// </para>
    /// <para>
    /// Ids and entry ids sit far outside any real AniList id, so they cannot collide with recorded
    /// data. The canary stays first and keeps its ids from before the others existed.
    /// </para>
    /// </summary>
    private static readonly MadeUpTitle[] Titles =
    [
        new(999_000_001, 999_000_002, Title, "ANIME", "TV", Episodes: 12, Chapters: null),
        new(999_000_011, 999_000_012, Marker + " - Midnight Parfait", "ANIME", "OVA", Episodes: 2, Chapters: null),
        new(999_000_021, 999_000_022, Marker + " - Velvet Sprinkles", "MANGA", "MANGA", Episodes: null, Chapters: 18),
    ];

    /// <summary>Every made-up title's name, the canary first.</summary>
    public static IReadOnlyList<string> MadeUpTitles { get; } = [.. Titles.Select(t => t.Name)];

    public static void Splice(string operationName, JsonNode? variables, JsonNode? response)
    {
        var data = response?["data"];
        if (data is null)
        {
            return;
        }

        var type = variables?["type"] is JsonValue value && value.TryGetValue<string>(out var named) ? named : null;

        if (string.Equals(operationName, "MediaListCollection", StringComparison.Ordinal))
        {
            SpliceIntoList(data, type);
            return;
        }

        if (DetailsSplices.TryGetValue(operationName, out var spliceDetails))
        {
            spliceDetails(data, ForType(type: null));
            return;
        }

        // false means "SFW only" and is the one case the canary must stay out of. true and absent
        // both mean adult content is permitted, which is exactly when a correct app should not be
        // asking — so that is when the canary is armed.
        if (variables?["isAdult"] is JsonValue isAdult
            && isAdult.TryGetValue<bool>(out var adultOnly)
            && !adultOnly)
        {
            return;
        }

        // Search names its type when it has one, and searches both when it does not. Browse and
        // Discover are anime by definition.
        var mediaType = string.Equals(operationName, "Search", StringComparison.Ordinal) ? type : type ?? "ANIME";
        SpliceIntoMediaArrays(data, ForType(mediaType));
    }

    /// <summary>
    /// The details of a made-up title, or false for anything else. Asked before the fixtures, which
    /// have no recording for these ids.
    /// </summary>
    public static bool TryAnswer(string operationName, JsonNode? variables, [NotNullWhen(true)] out JsonNode? response)
    {
        response = null;
        if (!string.Equals(operationName, "Media", StringComparison.Ordinal)
            || variables?["id"] is not JsonValue value
            || !value.TryGetValue<int>(out var id)
            || Array.Find(Titles, t => t.MediaId == id) is not { } title)
        {
            return false;
        }

        response = new JsonObject { ["data"] = new JsonObject { ["Media"] = Details(title) } };
        return true;
    }

    /// <summary>
    /// The titles of one type, and always the canary. A null type means both.
    /// <para>
    /// The canary ignores the type on purpose. It is the gate's marker, so it must stay armed on
    /// every list and every adult-permitted response, whatever is being asked for.
    /// </para>
    /// </summary>
    private static List<MadeUpTitle> ForType(string? type)
        => [.. Titles.Where((t, i) => i == 0 || type is null || string.Equals(t.Type, type, StringComparison.Ordinal))];

    private static void SpliceIntoList(JsonNode data, string? type)
    {
        // Rides the first list, whichever it is, so the client-side filter is exercised on the
        // section the Library renders first.
        if (data["MediaListCollection"]?["lists"] is JsonArray { Count: > 0 } lists
            && lists[0]?["entries"] is JsonArray entries)
        {
            // In that list's status, not a fixed one (#221). Replay reads a list's status off its
            // entries (MutationState.ReinsertSavedEntries), so a CURRENT canary under Completed made
            // Completed read as Watching: a completed title vanished, and a title moved to Watching
            // was filed under Completed.
            var status = entries
                .Select(e => e?["status"]?.GetValue<string>())
                .FirstOrDefault(s => s is not null) ?? "CURRENT";

            var titles = ForType(type);
            for (var i = titles.Count - 1; i >= 0; i--)
            {
                entries.Insert(0, ListEntry(titles[i], status));
            }
        }
    }

    /// <summary>
    /// The details pages (#199), always armed like the list. Every nested media connection on them
    /// comes back with 18+ titles whether or not the viewer is signed in, and none takes an
    /// <c>isAdult</c> argument, so the client-side filter (<c>AdultMediaFilter</c>) is all there is.
    /// <para>
    /// Targeted per operation rather than walked for, because the shapes differ: a relation wraps its
    /// media in <c>node</c>, a recommendation in <c>mediaRecommendation</c>, a studio lists bare
    /// <c>nodes</c>, and a voice role carries a whole array. A new details section has to be added
    /// here, and <c>AdultCanaryTests.DetailsSections</c> lists the ones that are.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, Action<JsonNode, List<MadeUpTitle>>> DetailsSplices = new(StringComparer.Ordinal)
    {
        ["Media"] = (data, titles) =>
        {
            IntoConnection(data["Media"]?["relations"], "edges", titles, t => new JsonObject { ["relationType"] = "SEQUEL", ["node"] = Summary(t) });
            IntoConnection(data["Media"]?["recommendations"], "nodes", titles, Recommendation);
        },
        ["MediaRecommendationsPage"] = (data, titles) => IntoConnection(data["Media"]?["recommendations"], "nodes", titles, Recommendation),
        ["Staff"] = (data, titles) =>
        {
            IntoVoiceRoles(data["Staff"]?["characters"], titles);
            IntoConnection(data["Staff"]?["staffMedia"], "edges", titles, StaffMediaEdge);
        },
        ["StaffCharactersPage"] = (data, titles) => IntoVoiceRoles(data["Staff"]?["characters"], titles),
        ["StaffMediaPage"] = (data, titles) => IntoConnection(data["Staff"]?["staffMedia"], "edges", titles, StaffMediaEdge),
        ["Character"] = (data, titles) => IntoConnection(data["Character"]?["media"], "edges", titles, CharacterMediaEdge),
        ["CharacterMediaPage"] = (data, titles) => IntoConnection(data["Character"]?["media"], "edges", titles, CharacterMediaEdge),
        ["Studio"] = (data, titles) => IntoConnection(data["Studio"]?["media"], "nodes", titles, Summary),
        ["StudioMediaPage"] = (data, titles) => IntoConnection(data["Studio"]?["media"], "nodes", titles, Summary),
    };

    /// <summary>The id of the made-up character who voices only made-up titles. Outside any real AniList id.</summary>
    private const int MadeUpCharacterId = 999_000_031;

    /// <summary>
    /// Puts the made-up titles at the front of a connection's items, each wrapped the way that
    /// connection wraps a media. A connection the query did not ask for stays absent.
    /// </summary>
    private static void IntoConnection(JsonNode? connection, string items, List<MadeUpTitle> titles, Func<MadeUpTitle, JsonObject> wrap)
    {
        if (connection?[items] is not JsonArray array)
        {
            return;
        }

        for (var i = titles.Count - 1; i >= 0; i--)
        {
            array.Insert(0, wrap(titles[i]));
        }
    }

    /// <summary>
    /// Arms both halves of the voice-role filter. Every real role gets the made-up titles ahead of its
    /// own, so a correct app shows the role's first real title; and one made-up role has nothing but
    /// made-up titles, so a correct app drops it. Its character's name carries the marker too, so a
    /// role kept with its media line blanked still fails the gate.
    /// </summary>
    private static void IntoVoiceRoles(JsonNode? characters, List<MadeUpTitle> titles)
    {
        if (characters?["edges"] is not JsonArray edges)
        {
            return;
        }

        SpliceIntoMediaArrays(edges, titles);

        var name = Marker + " - Made-up Role";
        edges.Insert(0, new JsonObject
        {
            ["node"] = new JsonObject
            {
                ["id"] = MadeUpCharacterId,
                ["name"] = new JsonObject { ["full"] = name, ["native"] = name, ["userPreferred"] = name },
                ["image"] = null,
                ["favourites"] = 0,
            },
            ["role"] = "MAIN",
            ["media"] = new JsonArray([.. titles.Select(t => (JsonNode)Summary(t))]),
        });
    }

    private static JsonObject Recommendation(MadeUpTitle title)
        => new() { ["rating"] = 0, ["mediaRecommendation"] = Summary(title) };

    private static JsonObject StaffMediaEdge(MadeUpTitle title)
        => new() { ["node"] = Summary(title), ["staffRole"] = "Original Creator" };

    private static JsonObject CharacterMediaEdge(MadeUpTitle title)
        => new() { ["node"] = Summary(title), ["characterRole"] = "MAIN", ["voiceActors"] = new JsonArray() };

    /// <summary>
    /// Walks for any <c>media</c> array. Search, BrowseAnime and every DiscoverSections alias share
    /// that shape, so one traversal covers all of them and a future section is covered by default
    /// rather than by remembering to add it here.
    /// </summary>
    private static void SpliceIntoMediaArrays(JsonNode node, List<MadeUpTitle> titles)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (property.Key == "media" && property.Value is JsonArray media)
                    {
                        for (var i = titles.Count - 1; i >= 0; i--)
                        {
                            media.Insert(0, Summary(titles[i]));
                        }

                        continue;
                    }

                    if (property.Value is not null)
                    {
                        SpliceIntoMediaArrays(property.Value, titles);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        SpliceIntoMediaArrays(item, titles);
                    }
                }

                break;
        }
    }

    private static JsonObject ListEntry(MadeUpTitle title, string status) => new()
    {
        ["id"] = title.EntryId,
        ["mediaId"] = title.MediaId,
        ["status"] = status,
        ["progress"] = 0,
        ["score"] = 0,
        ["media"] = Summary(title),
    };

    private static JsonObject Summary(MadeUpTitle title) => new()
    {
        ["id"] = title.MediaId,
        ["title"] = TitleObject(title),

        // No cover image on purpose: nothing to fetch and nothing to look at if the filter ever does
        // fail. The title is the entire payload.
        ["coverImage"] = null,
        ["format"] = title.Format,
        ["type"] = title.Type,
        ["status"] = "FINISHED",
        ["episodes"] = title.Episodes,
        ["chapters"] = title.Chapters,
        ["isAdult"] = true,
    };

    /// <summary>
    /// The details page's whole field set, with every connection present and empty. A missing
    /// connection is a different shape from an empty one, and the details mapping was written
    /// against what AniList sends.
    /// </summary>
    private static JsonObject Details(MadeUpTitle title)
    {
        var media = Summary(title);
        media["bannerImage"] = null;
        media["description"] = "A made-up 18+ title that exists only in the CI build's fixtures, so the "
            + "adult-content screens have something to show. Nothing real is behind it.";
        media["volumes"] = null;
        media["duration"] = title.Episodes is null ? null : 24;
        media["countryOfOrigin"] = "JP";
        media["genres"] = new JsonArray("Romance");
        media["synonyms"] = new JsonArray();
        media["tags"] = new JsonArray();
        media["rankings"] = new JsonArray();
        media["externalLinks"] = new JsonArray();
        media["nextAiringEpisode"] = null;
        media["trailer"] = null;
        media["isFavourite"] = false;
        media["studios"] = new JsonObject { ["edges"] = new JsonArray() };
        media["relations"] = new JsonObject { ["edges"] = new JsonArray() };
        media["characters"] = EmptyPage("edges");
        media["staff"] = EmptyPage("edges");
        media["recommendations"] = EmptyPage("nodes");
        media["stats"] = new JsonObject
        {
            ["scoreDistribution"] = new JsonArray(),
            ["statusDistribution"] = new JsonArray(),
        };

        // Every made-up title sits on the list, so its details say so, the way a real one's would.
        media["mediaListEntry"] = new JsonObject
        {
            ["id"] = title.EntryId,
            ["mediaId"] = title.MediaId,
            ["status"] = "CURRENT",
            ["progress"] = 0,
            ["score"] = 0,
            ["repeat"] = 0,
            ["notes"] = null,
            ["private"] = false,
            ["hiddenFromStatusLists"] = false,
        };

        return media;
    }

    private static JsonObject EmptyPage(string items) => new()
    {
        ["pageInfo"] = new JsonObject { ["hasNextPage"] = false, ["currentPage"] = 1 },
        [items] = new JsonArray(),
    };

    private static JsonObject TitleObject(MadeUpTitle title) => new()
    {
        ["romaji"] = title.Name,
        ["english"] = title.Name,
        ["native"] = title.Name,
        ["userPreferred"] = title.Name,
    };

    private sealed record MadeUpTitle(
        int MediaId, int EntryId, string Name, string Type, string Format, int? Episodes, int? Chapters);
}
#endif
