#if DEBUG
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
/// The two halves work differently, deliberately:
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

    /// <summary>Far outside any real AniList id, so it cannot collide with recorded data.</summary>
    private const int CanaryMediaId = 999_000_001;

    private const int CanaryEntryId = 999_000_002;

    public static void Splice(string operationName, JsonNode? variables, JsonNode? response)
    {
        var data = response?["data"];
        if (data is null)
        {
            return;
        }

        if (string.Equals(operationName, "MediaListCollection", StringComparison.Ordinal))
        {
            SpliceIntoList(data);
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

        SpliceIntoMediaArrays(data);
    }

    private static void SpliceIntoList(JsonNode data)
    {
        // Rides the first list, whichever it is, so the client-side filter is exercised on the
        // section the Library renders first.
        if (data["MediaListCollection"]?["lists"] is JsonArray { Count: > 0 } lists
            && lists[0]?["entries"] is JsonArray entries)
        {
            entries.Insert(0, CanaryListEntry());
        }
    }

    /// <summary>
    /// Walks for any <c>media</c> array. Search, BrowseAnime and every DiscoverSections alias share
    /// that shape, so one traversal covers all of them and a future section is covered by default
    /// rather than by remembering to add it here.
    /// </summary>
    private static void SpliceIntoMediaArrays(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (property.Key == "media" && property.Value is JsonArray media)
                    {
                        media.Insert(0, CanaryBrowseItem());
                        continue;
                    }

                    if (property.Value is not null)
                    {
                        SpliceIntoMediaArrays(property.Value);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        SpliceIntoMediaArrays(item);
                    }
                }

                break;
        }
    }

    private static JsonObject CanaryListEntry() => new()
    {
        ["id"] = CanaryEntryId,
        ["mediaId"] = CanaryMediaId,
        ["status"] = "CURRENT",
        ["progress"] = 0,
        ["score"] = 0,
        ["media"] = CanaryMedia(),
    };

    private static JsonObject CanaryBrowseItem() => CanaryMedia();

    private static JsonObject CanaryMedia() => new()
    {
        ["id"] = CanaryMediaId,
        ["title"] = new JsonObject
        {
            ["romaji"] = Title,
            ["english"] = Title,
            ["native"] = Title,
            ["userPreferred"] = Title,
        },

        // No cover image on purpose: nothing to fetch and nothing to look at if the filter ever does
        // fail. The title is the entire payload.
        ["coverImage"] = null,
        ["format"] = "TV",
        ["type"] = "ANIME",
        ["status"] = "FINISHED",
        ["isAdult"] = true,
    };
}
#endif
