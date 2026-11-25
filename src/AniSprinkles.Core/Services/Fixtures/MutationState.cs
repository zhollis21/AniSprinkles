#if DEBUG
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;


namespace AniSprinkles.Services.Fixtures;

/// <summary>
/// Answers AniList's mutations, and remembers what they did (#134).
/// <para>
/// Mutations are the one thing that cannot be recorded: a recording of
/// <c>SaveMediaListEntry</c> would be a recording of a change made to a real account, and replaying
/// it would answer every future edit with one stale result. So the reads are real data and the
/// writes are modelled here.
/// </para>
/// <para>
/// Remembering matters as much as answering. A CI pass taps "remove from list" and then navigates
/// back to the Library; if the recorded list still contains the entry, the app looks broken for a
/// reason that has nothing to do with the app. <see cref="Apply"/> folds the accumulated writes into
/// each read on the way out, which is what makes the two consistent.
/// </para>
/// </summary>
public sealed class MutationState
{
    /// <summary>
    /// Deletions, held as <em>media</em> ids rather than list-entry ids.
    /// <para>
    /// The two mutations speak different languages: <c>DeleteMediaListEntry</c> takes the list entry's
    /// id, while <c>SaveMediaListEntry</c> takes the media id and, on a real server, would mint a new
    /// entry id. Tracking entry ids means a re-add can never cancel a delete — the ids simply never
    /// match — and the re-added title stays invisible in the Library. Media id is the identity that
    /// survives the round trip.
    /// </para>
    /// </summary>
    private readonly HashSet<int> _deletedMediaIds = [];

    /// <summary>
    /// Entry id → media id, learned from the list responses that pass through <see cref="Apply"/>.
    /// <para>
    /// Delete only ever names an entry id, so the translation has to come from somewhere. It comes
    /// free: the list has to be read before anything in it can be deleted, and that response carries
    /// both ids for every entry.
    /// </para>
    /// </summary>
    private readonly Dictionary<int, int> _mediaIdByEntryId = [];

    /// <summary>
    /// Media id → the entry <c>SaveMediaListEntry</c> answered with, replayed into later list reads.
    /// <para>
    /// Deletes alone are not enough. A save that is not remembered means an added title reports
    /// success and then vanishes from the next <c>MediaListCollection</c>, and a title moved between
    /// statuses stays in its old section after the list reloads — the app looks broken for a reason
    /// that has nothing to do with the app, which is the whole thing <see cref="Apply"/> exists to
    /// prevent.
    /// </para>
    /// </summary>
    private readonly Dictionary<int, JsonObject> _savedEntriesByMediaId = [];

    /// <summary>
    /// Media id → the entry the most recent read showed, field by field across every shape a read
    /// selects. A save names only what the app sent, and AniList keeps a field sent as null; this is
    /// what the save's answer, and every read after it, fills those fields from.
    /// </summary>
    private readonly Dictionary<int, JsonObject> _lastSeenEntryByMediaId = [];

    /// <summary>What <c>MediaQuery</c> selects for the details page's own entry.</summary>
    private static readonly string[] DetailsEntryFields =
        ["id", "mediaId", "status", "progress", "progressVolumes", "score", "repeat", "notes", "private", "hiddenFromStatusLists", "updatedAt"];

    /// <summary>What <c>MediaRelationsQuery</c> selects for each related title's entry.</summary>
    private static readonly string[] RelationEntryFields =
        ["id", "status", "progress", "progressVolumes", "score", "repeat", "notes", "private", "hiddenFromStatusLists"];

    /// <summary>What every other card query selects: Search, Discover, View All, the people and studio pages.</summary>
    private static readonly string[] CardEntryFields = ["id", "status", "progress", "score"];

    /// <summary>
    /// Every related title a replayed <c>MediaRelations</c> read showed, by media id: the fallback
    /// media block for a save when no <c>Media</c> was recorded for it (#221). Up Next adds titles
    /// from exactly that read, and most of them were never opened while recording, so without this
    /// the Library drew the added title as a blank card.
    /// </summary>
    private readonly Dictionary<int, JsonObject> _relatedMediaById = [];

    private readonly JsonObject _viewerOverrides = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Whether this operation is a mutation, and if so what AniList would have replied.
    /// </summary>
    public bool TryAnswer(
        string operationName,
        JsonNode? variables,
        IFixtureLookup fixtures,
        [NotNullWhen(true)] out JsonNode? response)
    {
        switch (operationName)
        {
            case "SaveMediaListEntry":
                response = SaveEntry(variables, fixtures);
                return true;

            case "DeleteMediaListEntry":
                response = DeleteEntry(variables);
                return true;

            case "ToggleFavourite":
                // The app only reads success from this; the counts are never rendered.
                response = Data("ToggleFavourite", new JsonObject
                {
                    ["anime"] = new JsonObject { ["pageInfo"] = new JsonObject { ["total"] = 1 } },
                    ["manga"] = new JsonObject { ["pageInfo"] = new JsonObject { ["total"] = 1 } },
                });
                return true;

            case "UpdateUser":
                response = UpdateUser(variables, fixtures);
                return true;

            default:
                response = null;
                return false;
        }
    }

    /// <summary>Folds accumulated writes into an outgoing read.</summary>
    public void Apply(string operationName, JsonNode response)
    {
        var data = response["data"];
        if (data is null)
        {
            return;
        }

        switch (operationName)
        {
            case "MediaListCollection":
                RemoveDeletedEntries(data);
                break;

            // ViewerFull is what Settings reloads from, so a preference change that did not survive
            // it would revert on the next visit — which is exactly the bug that made Display
            // Preferences unverifiable in CI before (#130).
            case "ViewerFull":
            case "Viewer":
                ApplyViewerOverrides(data["Viewer"]);
                break;

            case "MediaRelations":
                RememberRelatedMedia(data);
                break;
        }

        // Every card and page that shows the viewer's entry: the details page, Search, Discover,
        // View All, relations, the people and studio pages (#225). Without it a title saved or
        // removed during replay read back as recorded everywhere but the Library, and a removal made
        // from one of them, before any list read, could not even be matched to its title.
        ApplyToMediaListEntries(data, operationName);
    }

    /// <summary>
    /// Finds every media node carrying a <c>mediaListEntry</c>, learns its entry id, and puts the
    /// accumulated writes onto it: null for a removed title, the saved entry for a saved one.
    /// </summary>
    private void ApplyToMediaListEntries(JsonNode data, string operationName)
    {
        lock (_gate)
        {
            Visit(data, data, operationName);
        }
    }

    private void Visit(JsonNode? node, JsonNode data, string operationName)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array)
                {
                    Visit(child, data, operationName);
                }

                break;

            case JsonObject obj:
                if (obj.ContainsKey("mediaListEntry") && obj["id"] is JsonValue idValue
                    && idValue.TryGetValue<int>(out var mediaId))
                {
                    Reconcile(obj, mediaId, FieldsFor(obj, data, operationName));
                }

                foreach (var (key, child) in obj.ToList())
                {
                    if (key != "mediaListEntry")
                    {
                        Visit(child, data, operationName);
                    }
                }

                break;
        }
    }

    /// <summary>The fields the query that produced <paramref name="mediaNode"/> selects for its entry.</summary>
    private static string[] FieldsFor(JsonObject mediaNode, JsonNode data, string operationName)
        => operationName switch
        {
            "Media" when ReferenceEquals(mediaNode, data["Media"]) => DetailsEntryFields,
            "MediaRelations" when !ReferenceEquals(mediaNode, data["Media"]) => RelationEntryFields,
            _ => CardEntryFields,
        };

    private void Reconcile(JsonObject mediaNode, int mediaId, string[] fields)
    {
        var recorded = mediaNode["mediaListEntry"] as JsonObject;
        if (recorded?["id"] is JsonValue entryIdValue && entryIdValue.TryGetValue<int>(out var entryId))
        {
            _mediaIdByEntryId[entryId] = mediaId;
        }

        if (_deletedMediaIds.Contains(mediaId))
        {
            mediaNode["mediaListEntry"] = null;
        }
        else if (_savedEntriesByMediaId.TryGetValue(mediaId, out var saved))
        {
            mediaNode["mediaListEntry"] = Project(saved, recorded, recorded is null ? fields : recorded.Select(p => p.Key));
        }
        else if (recorded is not null)
        {
            RememberSeen(mediaId, recorded);
        }
    }

    /// <summary>
    /// The saved entry in the shape a read selects — the recorded entry's own fields where it has
    /// one — with any field the save left null taken from the recording, as AniList keeps it.
    /// </summary>
    private static JsonObject Project(JsonObject saved, JsonObject? recorded, IEnumerable<string> fields)
    {
        var projected = new JsonObject();
        foreach (var field in fields)
        {
            projected[field] = (saved[field] ?? recorded?[field])?.DeepClone();
        }

        return projected;
    }

    /// <summary>Folds what a read showed into <see cref="_lastSeenEntryByMediaId"/>, field by field.</summary>
    private void RememberSeen(int mediaId, JsonObject entry)
    {
        if (!_lastSeenEntryByMediaId.TryGetValue(mediaId, out var seen))
        {
            seen = [];
            _lastSeenEntryByMediaId[mediaId] = seen;
        }

        foreach (var (key, value) in entry)
        {
            if (key != "media")
            {
                seen[key] = value?.DeepClone();
            }
        }
    }

    private void RememberRelatedMedia(JsonNode data)
    {
        if (data["Media"]?["relations"]?["edges"] is not JsonArray edges)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var edge in edges)
            {
                if (edge?["node"] is JsonObject node && node["id"]?.GetValue<int>() is { } id)
                {
                    _relatedMediaById[id] = (JsonObject)node.DeepClone();
                }
            }
        }
    }

    private JsonNode SaveEntry(JsonNode? variables, IFixtureLookup fixtures)
    {
        var mediaId = variables?["mediaId"]?.GetValue<int>() ?? 0;

        lock (_gate)
        {
            // What AniList already holds for this title: the last save, else the last read. A field
            // the save sends as null keeps its value there, and an existing entry keeps its id.
            var known = _savedEntriesByMediaId.TryGetValue(mediaId, out var previous)
                ? previous
                : _lastSeenEntryByMediaId.GetValueOrDefault(mediaId);
            var entry = (JsonObject?)known?.DeepClone() ?? [];

            // AniList would allocate one for a new entry; anything stable and non-colliding does here.
            entry["id"] ??= 900_000_000 + mediaId;
            entry["mediaId"] = mediaId;
            foreach (var field in SavedFields)
            {
                if (variables?[field] is { } value)
                {
                    entry[field] = value.DeepClone();
                }
                else if (!entry.ContainsKey(field))
                {
                    entry[field] = null;
                }
            }

            entry["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Borrow the media block from the recording for this id rather than inventing one. The
            // real mutation returns it, and a null here would blank the card the caller re-renders.
            entry["media"] = MediaBlockFor(mediaId, fixtures);

            // Saving un-deletes, which is what the app's undo path expects. Keyed on media id so it
            // actually cancels the matching delete — see _deletedMediaIds.
            _deletedMediaIds.Remove(mediaId);
            _savedEntriesByMediaId[mediaId] = (JsonObject)entry.DeepClone();

            // A list read learns entry ids from the recording, before saved entries go back in, so it
            // never sees a made-up one. Without it a title added during replay could not be removed.
            if (entry["id"]?.GetValue<int>() is { } entryId)
            {
                _mediaIdByEntryId[entryId] = mediaId;
            }

            return Data("SaveMediaListEntry", entry);
        }
    }

    /// <summary>The mutation's variables that are entry fields, as <c>SaveEntryMutation</c> declares them.</summary>
    private static readonly string[] SavedFields =
        ["status", "progress", "progressVolumes", "score", "repeat", "notes", "private", "hiddenFromStatusLists"];

    private JsonNode? MediaBlockFor(int mediaId, IFixtureLookup fixtures)
    {
        var key = GraphQlFixtureKey.Derive("Media", new JsonObject { ["id"] = mediaId });
        // Null fingerprint: this knows which media it wants but not the query text that recorded
        // it. Media has one query, so the address is unambiguous and this resolves.
        if (fixtures.TryGet(key, null, out var fixture))
        {
            return fixture.Response?["data"]?["Media"]?.DeepClone();
        }

        lock (_gate)
        {
            return _relatedMediaById.TryGetValue(mediaId, out var related) ? related.DeepClone() : null;
        }
    }

    private JsonNode DeleteEntry(JsonNode? variables)
    {
        if (variables?["id"]?.GetValue<int>() is { } entryId)
        {
            lock (_gate)
            {
                // Nothing to translate with means nothing to hide: the list has not been read, so
                // there is no rendered entry this delete could be inconsistent with.
                if (_mediaIdByEntryId.TryGetValue(entryId, out var mediaId))
                {
                    _deletedMediaIds.Add(mediaId);
                }
            }
        }

        return Data("DeleteMediaListEntry", new JsonObject { ["deleted"] = true });
    }

    private JsonNode UpdateUser(JsonNode? variables, IFixtureLookup fixtures)
    {
        lock (_gate)
        {
            foreach (var property in variables?.AsObject() ?? [])
            {
                if (property.Value is null)
                {
                    continue;
                }

                // displayAdultContent is deliberately not honoured. The canary gate depends on the
                // viewer reporting adult content off, and that must not be flippable from inside the
                // app — otherwise a CI run could disarm its own safety check.
                if (string.Equals(property.Key, "displayAdultContent", StringComparison.Ordinal))
                {
                    continue;
                }

                _viewerOverrides[property.Key] = property.Value.DeepClone();
            }
        }

        // The mutation returns the same shape ViewerFull does, so the recorded viewer with the
        // overrides applied *is* the correct answer — no hand-built user object.
        var viewerKey = GraphQlFixtureKey.Derive("ViewerFull", null);
        var viewer = fixtures.TryGet(viewerKey, null, out var fixture)
            ? fixture.Response?["data"]?["Viewer"]?.DeepClone()
            : null;

        ApplyViewerOverrides(viewer);
        return Data("UpdateUser", viewer);
    }

    /// <summary>
    /// Learns each entry's media id and drops anything deleted.
    /// <para>
    /// Both halves in one pass, because they read the same entries. The learning is unconditional —
    /// it has to happen on the first read, before any delete exists to act on.
    /// </para>
    /// </summary>
    private void RemoveDeletedEntries(JsonNode data)
    {
        // The recorded entries a saved one replaces, for the fields the save left null.
        var recorded = new Dictionary<int, JsonObject>();
        lock (_gate)
        {
            foreach (var list in data["MediaListCollection"]?["lists"]?.AsArray() ?? [])
            {
                if (list?["entries"] is not JsonArray entries)
                {
                    continue;
                }

                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var entry = entries[i];
                    if (entry?["mediaId"]?.GetValue<int>() is not { } mediaId)
                    {
                        continue;
                    }

                    if (entry["id"]?.GetValue<int>() is { } entryId)
                    {
                        _mediaIdByEntryId[entryId] = mediaId;
                    }

                    // A saved entry is removed here and re-inserted below, into whichever list its
                    // new status belongs to. Removing first is what makes a status change a *move*
                    // rather than a duplicate in two sections.
                    if (_deletedMediaIds.Contains(mediaId) || _savedEntriesByMediaId.ContainsKey(mediaId))
                    {
                        if (entry is JsonObject recordedEntry)
                        {
                            recorded[mediaId] = recordedEntry;
                        }

                        entries.RemoveAt(i);
                    }
                    else if (entry is JsonObject seen)
                    {
                        RememberSeen(mediaId, seen);
                    }
                }
            }

            ReinsertSavedEntries(data, recorded);
        }
    }

    /// <summary>
    /// Puts each saved entry back into the list its status belongs to — adding it if the recording
    /// never had it.
    /// <para>
    /// The target list is found by looking at what the other entries in each list say their status
    /// is, rather than by matching list names. Names are the user's, not the API's: the anime list
    /// is "Watching" where the manga one is "Reading", and either can be renamed or replaced by
    /// custom lists. The statuses are a fixed enum, so they are the reliable half.
    /// </para>
    /// <para>
    /// An entry whose status has no corresponding list is left out rather than dropped somewhere
    /// arbitrary — moving a title to Rewatching when the recording has no Rewatching section has no
    /// right answer, and inventing one would put it under a heading it does not belong to.
    /// </para>
    /// </summary>
    private void ReinsertSavedEntries(JsonNode data, IReadOnlyDictionary<int, JsonObject> recorded)
    {
        if (_savedEntriesByMediaId.Count == 0)
        {
            return;
        }

        var lists = data["MediaListCollection"]?["lists"]?.AsArray();
        if (lists is null)
        {
            return;
        }

        foreach (var (mediaId, saved) in _savedEntriesByMediaId)
        {
            if (_deletedMediaIds.Contains(mediaId))
            {
                continue;
            }

            var status = saved["status"]?.GetValue<string>();
            if (status is null)
            {
                continue;
            }

            foreach (var list in lists)
            {
                if (list?["entries"] is not JsonArray entries || entries.Count == 0)
                {
                    continue;
                }

                // Any entry, not just the first: the list's status is a property of the list, but
                // reading it off one arbitrary element makes the match hostage to whatever happens
                // to be at index 0 — including an entry that was itself just moved.
                var listStatus = entries
                    .Select(e => e?["status"]?.GetValue<string>())
                    .FirstOrDefault(s => s is not null);

                if (!string.Equals(listStatus, status, StringComparison.Ordinal))
                {
                    continue;
                }

                // Front of the list: the entry was just touched, and a freshly changed title
                // appearing at the top is both easier to see in a screenshot and closer to what
                // AniList's own default ordering does with recently updated entries.
                // In the list query's own shape, as the recorded entries in it are, with the
                // recording's media block where it had one: a Media read's block is a different shape.
                var original = recorded.GetValueOrDefault(mediaId);
                var template = original ?? entries.OfType<JsonObject>().FirstOrDefault();
                var projected = template is null
                    ? (JsonObject)saved.DeepClone()
                    : Project(saved, original, template.Select(p => p.Key).Append("media").Distinct());
                if (original?["media"] is { } recordedMedia)
                {
                    projected["media"] = recordedMedia.DeepClone();
                }

                entries.Insert(0, projected);
                break;
            }
        }
    }

    /// <summary>
    /// Writes the accumulated <c>UpdateUser</c> changes onto a viewer object. The variable names come
    /// from the mutation's arguments and mostly live under <c>options</c>; score format is the odd
    /// one out, on <c>mediaListOptions</c>.
    /// </summary>
    private void ApplyViewerOverrides(JsonNode? viewer)
    {
        if (viewer is null)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var (key, value) in _viewerOverrides)
            {
                if (value is null)
                {
                    continue;
                }

                if (string.Equals(key, "scoreFormat", StringComparison.Ordinal))
                {
                    if (viewer["mediaListOptions"] is JsonObject listOptions)
                    {
                        listOptions["scoreFormat"] = value.DeepClone();
                    }

                    continue;
                }

                if (viewer["options"] is JsonObject options)
                {
                    options[key] = value.DeepClone();
                }
            }
        }
    }

    private static JsonNode Data(string field, JsonNode? value)
        => new JsonObject { ["data"] = new JsonObject { [field] = value } };
}
#endif
