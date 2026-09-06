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
        }
    }

    private JsonNode SaveEntry(JsonNode? variables, IFixtureLookup fixtures)
    {
        var mediaId = variables?["mediaId"]?.GetValue<int>() ?? 0;

        var entry = new JsonObject
        {
            // AniList would allocate one; anything stable and non-colliding does here.
            ["id"] = 900_000_000 + mediaId,
            ["mediaId"] = mediaId,
            ["status"] = variables?["status"]?.DeepClone(),
            ["progress"] = variables?["progress"]?.DeepClone(),
            ["progressVolumes"] = variables?["progressVolumes"]?.DeepClone(),
            ["score"] = variables?["score"]?.DeepClone(),
            ["repeat"] = variables?["repeat"]?.DeepClone(),
            ["notes"] = variables?["notes"]?.DeepClone(),
            ["private"] = variables?["private"]?.DeepClone(),
            ["hiddenFromStatusLists"] = variables?["hiddenFromStatusLists"]?.DeepClone(),
            ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),

            // Borrow the media block from the recording for this id rather than inventing one. The
            // real mutation returns it, and a null here would blank the card the caller re-renders.
            ["media"] = MediaBlockFor(mediaId, fixtures),
        };

        // Saving un-deletes, which is what the app's undo path expects. Keyed on media id so it
        // actually cancels the matching delete — see _deletedMediaIds.
        lock (_gate)
        {
            _deletedMediaIds.Remove(mediaId);
        }

        return Data("SaveMediaListEntry", entry);
    }

    private static JsonNode? MediaBlockFor(int mediaId, IFixtureLookup fixtures)
    {
        var key = GraphQlFixtureKey.Derive("Media", new JsonObject { ["id"] = mediaId });
        // Null fingerprint: this knows which media it wants but not the query text that recorded
        // it. Media has one query, so the address is unambiguous and this resolves.
        return fixtures.TryGet(key, null, out var fixture)
            ? fixture.Response?["data"]?["Media"]?.DeepClone()
            : null;
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

                    if (_deletedMediaIds.Contains(mediaId))
                    {
                        entries.RemoveAt(i);
                    }
                }
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
