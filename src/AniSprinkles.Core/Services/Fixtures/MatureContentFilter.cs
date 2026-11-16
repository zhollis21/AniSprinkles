#if DEBUG
using System.Text.Json.Nodes;

namespace AniSprinkles.Services.Fixtures;

/// <summary>
/// Keeps Ecchi and 18+ titles out of the recorded fixtures.
/// <para>
/// The fixtures are committed to a public repository and rendered into CI screenshots, so the
/// recorder (<c>tools/record-anilist-fixtures.cs</c>) runs every response through this before it is
/// kept. Scrubbing covers was not enough: AniList does not flag Ecchi titles adult, so one on the
/// test account's own list (The Qwaser of Stigmata II) went into the Library fixture in full, with
/// its cover. The CI build's adult content is now entirely made up; see <c>AdultCanary</c>.
/// </para>
/// <para>
/// Lives in Core rather than in the tool so the rules can be tested. Most responses carry neither
/// <c>isAdult</c> nor <c>genres</c> on their nested titles, so this only knows the shapes. Deciding
/// which ids to exclude takes a lookup, which the recorder does.
/// </para>
/// </summary>
public static class MatureContentFilter
{
    /// <summary>
    /// AniList's genre for fan-service content. It is not adult by AniList's own flag, which is
    /// precisely why it needs a rule of its own.
    /// </summary>
    public const string EcchiGenre = "Ecchi";

    public static bool IsExcluded(bool? isAdult, IEnumerable<string>? genres)
        => isAdult == true
            || (genres?.Contains(EcchiGenre, StringComparer.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Every media id a response mentions, at any depth: each media object, and each
    /// <c>mediaId</c> (list and airing entries carry one).
    /// </summary>
    public static IReadOnlySet<int> MediaIdsIn(JsonNode? response)
    {
        var ids = new HashSet<int>();
        Collect(response, ids);
        return ids;
    }

    /// <summary>
    /// Removes every array element that is an excluded media, wraps one directly (a list entry's
    /// <c>media</c>, a relation's <c>node</c>, a recommendation's <c>mediaRecommendation</c>), or
    /// names one by <c>mediaId</c>. Returns how many were removed.
    /// <para>
    /// The element goes, not just the media inside it: a list entry or relation edge with its media
    /// nulled out renders as a blank row.
    /// </para>
    /// <para>
    /// Also drops every tag AniList flags adult. Those appear on titles that are not adult
    /// themselves, and their descriptions are explicit.
    /// </para>
    /// </summary>
    public static int Remove(JsonNode? response, IReadOnlySet<int> excluded)
        => RemoveFrom(response, excluded);

    /// <summary>
    /// Whether the whole fixture is about an excluded title: its details, or one of its section
    /// pages. Nothing in such a fixture is worth keeping, so the recorder deletes the file instead of
    /// filtering it.
    /// <para>
    /// Section pages select only the connection being paged, without the media's own id, so the id
    /// comes from the request's variables then. That fallback only applies under <c>data.Media</c>.
    /// A <c>Character</c> or <c>Staff</c> fixture also has an <c>id</c> variable, from a separate
    /// number range that overlaps media ids.
    /// </para>
    /// </summary>
    public static bool IsAbout(JsonNode? response, JsonNode? variables, IReadOnlySet<int> excluded)
    {
        if (response?["data"]?["Media"] is not JsonObject media)
        {
            return false;
        }

        var id = IntOf(media["id"]) ?? IntOf(variables?["id"]);
        return id is { } value && excluded.Contains(value);
    }

    /// <summary>
    /// A media object, as opposed to a character, staff member or studio: those have a
    /// <c>name</c>. Media have a <c>title</c> object.
    /// </summary>
    private static int? MediaIdOf(JsonNode? node)
        => node is JsonObject obj && obj["title"] is JsonObject ? IntOf(obj["id"]) : null;

    private static int? IntOf(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static void Collect(JsonNode? node, HashSet<int> ids)
    {
        switch (node)
        {
            case JsonObject obj:
                if (MediaIdOf(obj) is { } id)
                {
                    ids.Add(id);
                }

                if (IntOf(obj["mediaId"]) is { } mediaId)
                {
                    ids.Add(mediaId);
                }

                foreach (var property in obj)
                {
                    Collect(property.Value, ids);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    Collect(item, ids);
                }

                break;
        }
    }

    private static int RemoveFrom(JsonNode? node, IReadOnlySet<int> excluded)
    {
        var removed = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (property.Key == "tags" && property.Value is JsonArray tags)
                    {
                        removed += tags.RemoveAll(tag => tag?["isAdult"] is JsonValue flag
                            && flag.TryGetValue<bool>(out var isAdult)
                            && isAdult);
                    }

                    removed += RemoveFrom(property.Value, excluded);
                }

                break;

            case JsonArray array:
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    if (Mentions(array[i], excluded))
                    {
                        array.RemoveAt(i);
                        removed++;
                    }
                    else
                    {
                        removed += RemoveFrom(array[i], excluded);
                    }
                }

                break;
        }

        return removed;
    }

    /// <summary>
    /// Whether an array element is, wraps or names an excluded media. Only checks one level down on
    /// purpose. A media's details reach other titles through relations and recommendations, and
    /// matching at any depth would drop a perfectly ordinary title for having a sequel that is not.
    /// </summary>
    private static bool Mentions(JsonNode? element, IReadOnlySet<int> excluded)
    {
        if (element is not JsonObject obj)
        {
            return false;
        }

        if (MediaIdOf(obj) is { } id && excluded.Contains(id))
        {
            return true;
        }

        if (IntOf(obj["mediaId"]) is { } mediaId && excluded.Contains(mediaId))
        {
            return true;
        }

        foreach (var property in obj)
        {
            if (MediaIdOf(property.Value) is { } wrapped && excluded.Contains(wrapped))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
