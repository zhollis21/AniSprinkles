using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using AniSprinkles.Services.Fixtures;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The writes a CI build cannot replay (#134).
/// <para>
/// Mutations are the one thing that cannot be recorded — a recording of <c>SaveMediaListEntry</c>
/// would be a recording of a change made to a real account, and replaying it would answer every
/// future edit with one stale result. So reads are real data and writes are modelled, which means
/// the modelling is code that can be wrong while every fixture is perfect.
/// </para>
/// </summary>
public class MutationStateTests
{
    // ── Delete, and what a read shows afterwards ─────────────────────

    [Test]
    public void ADeletedEntry_DisappearsFromTheNextListRead()
    {
        // The lived failure this prevents: tap "remove from list", navigate back to the Library, and
        // the entry is still there because the recorded list never heard about the delete. The app
        // looks broken for a reason that has nothing to do with the app.
        var state = new MutationState();
        var list = ListResponse();
        state.Apply("MediaListCollection", list);

        Assert.That(state.TryAnswer("DeleteMediaListEntry", Vars("""{"id":1001}"""), NoFixtures, out _), Is.True);

        var reread = ListResponse();
        state.Apply("MediaListCollection", reread);

        Assert.That(reread.ToJsonString(), Does.Not.Contain("\"mediaId\":21"));
        Assert.That(reread.ToJsonString(), Does.Contain("\"mediaId\":16498"));
    }

    [Test]
    public void SavingAfterDeleting_BringsTheEntryBack()
    {
        // Regression test. Delete names the list-entry id; save names the media id and would, on a
        // real server, mint a brand new entry id. Tracked as entry ids the two can never match, so
        // the un-delete silently does nothing and a re-added title stays invisible in the Library.
        var state = new MutationState();
        state.Apply("MediaListCollection", ListResponse());

        state.TryAnswer("DeleteMediaListEntry", Vars("""{"id":1001}"""), NoFixtures, out _);
        state.TryAnswer("SaveMediaListEntry", Vars("""{"mediaId":21,"status":"CURRENT"}"""), NoFixtures, out _);

        var reread = ListResponse();
        state.Apply("MediaListCollection", reread);

        Assert.That(reread.ToJsonString(), Does.Contain("\"mediaId\":21"));
    }

    [Test]
    public void DeletingBeforeAnyListRead_ChangesNothing()
    {
        // Without a read there is no entry-id-to-media-id translation, and nothing rendered that the
        // delete could be inconsistent with. Guessing would be worse than doing nothing.
        var state = new MutationState();

        Assert.That(state.TryAnswer("DeleteMediaListEntry", Vars("""{"id":1001}"""), NoFixtures, out var response), Is.True);
        Assert.That(response, Is.Not.Null);
        Assert.That(response["data"]!["DeleteMediaListEntry"]!["deleted"]!.GetValue<bool>(), Is.True);

        var list = ListResponse();
        state.Apply("MediaListCollection", list);
        Assert.That(list.ToJsonString(), Does.Contain("\"mediaId\":21"));
    }

    // ── Saves, and what a read shows afterwards ──────────────────────

    [Test]
    public void AStatusChange_MovesTheEntryToTheMatchingSection()
    {
        // Caught by review, not by the tests above: those only covered delete-then-save for a title
        // already in the list, which passes without saves being recorded at all. A move needs the
        // save itself folded into the read, or the title sits in its old section after the reload
        // and the app looks broken for a reason that is not the app.
        var state = new MutationState();
        state.Apply("MediaListCollection", TwoSectionList());

        state.TryAnswer(
            "SaveMediaListEntry",
            Vars("""{"mediaId":21,"status":"COMPLETED","progress":1100}"""),
            NoFixtures,
            out _);

        var reread = TwoSectionList();
        state.Apply("MediaListCollection", reread);

        var lists = reread["data"]!["MediaListCollection"]!["lists"]!.AsArray();
        Assert.That(lists[0]!.ToJsonString(), Does.Not.Contain("\"mediaId\":21"));
        Assert.That(lists[1]!.ToJsonString(), Does.Contain("\"mediaId\":21"));
    }

    [Test]
    public void ATitleAbsentFromTheRecording_AppearsAfterBeingSaved()
    {
        // "Add to List" on an off-list media. Without the save being remembered this reported
        // success and then the title was simply gone from the next read.
        var state = new MutationState();
        state.Apply("MediaListCollection", TwoSectionList());

        state.TryAnswer(
            "SaveMediaListEntry",
            Vars("""{"mediaId":99999,"status":"CURRENT","progress":0}"""),
            NoFixtures,
            out _);

        var reread = TwoSectionList();
        state.Apply("MediaListCollection", reread);

        Assert.That(reread.ToJsonString(), Does.Contain("\"mediaId\":99999"));
    }

    [Test]
    public void ATitleAddedDuringReplay_CanBeRemovedAgain()
    {
        // An added title gets an entry id replay makes up, and that id reaches the app through the
        // next list read. Removing the title names it, so replay has to know which media it is, or
        // the title stays on the list after "Remove from list".
        var state = new MutationState();
        state.Apply("MediaListCollection", TwoSectionList());
        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":99999,"status":"CURRENT"}"""), NoFixtures, out var saved);
        state.Apply("MediaListCollection", TwoSectionList());

        var entryId = saved?["data"]?["SaveMediaListEntry"]?["id"]?.GetValue<int>();
        Assert.That(entryId, Is.Not.Null, "The save returned no entry id.");
        state.TryAnswer("DeleteMediaListEntry", Vars($$"""{"id":{{entryId}}}"""), NoFixtures, out _);

        var reread = TwoSectionList();
        state.Apply("MediaListCollection", reread);
        Assert.That(reread.ToJsonString(), Does.Not.Contain("\"mediaId\":99999"));
    }

    [Test]
    public void ASavedEntry_IsNotDuplicatedAcrossSections()
    {
        // The move is a remove-then-insert, so a bug here shows up as the same title under two
        // headings rather than as a missing one.
        var state = new MutationState();
        state.Apply("MediaListCollection", TwoSectionList());

        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":21,"status":"COMPLETED"}"""), NoFixtures, out _);

        var reread = TwoSectionList();
        state.Apply("MediaListCollection", reread);

        var occurrences = reread.ToJsonString().Split("\"mediaId\":21").Length - 1;
        Assert.That(occurrences, Is.EqualTo(1));
    }

    [Test]
    public void ASavedEntryWithNoMatchingSection_IsLeftOutRatherThanMisfiled()
    {
        // Nowhere right to put it: the recording has no Rewatching list. Filing it under an
        // unrelated heading would be worse than its absence, which is at least visible.
        var state = new MutationState();
        state.Apply("MediaListCollection", TwoSectionList());

        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":21,"status":"REPEATING"}"""), NoFixtures, out _);

        var reread = TwoSectionList();
        state.Apply("MediaListCollection", reread);

        Assert.That(reread.ToJsonString(), Does.Not.Contain("\"mediaId\":21"));
    }

    // ── Saves, with the adult canary spliced in (#221) ───────────────

    [TestCase(21, "COMPLETED", 0, TestName = "AMoveToCompleted_LandsUnderCompleted_WhenTheCanaryRidesIt")]
    [TestCase(5114, "CURRENT", 1, TestName = "AMoveToWatching_LandsUnderWatching_WhenTheCanaryRidesCompleted")]
    public void AMove_LandsInItsSection_WithTheCanarySplicedFirst(int mediaId, string status, int expectedList)
    {
        // The test account's first list is Completed, and the canary rides the first list. It used to
        // ride it as CURRENT, so replay read Completed as the Watching list: a completed title matched
        // no list and vanished, and a title moved to Watching was filed under Completed. Replay order
        // is the handler's: splice, then apply.
        var state = new MutationState();
        state.Apply("MediaListCollection", ReplayRead(CompletedFirstList()));

        state.TryAnswer(
            "SaveMediaListEntry", Vars($$"""{"mediaId":{{mediaId}},"status":"{{status}}"}"""), NoFixtures, out _);

        var reread = CompletedFirstList();
        state.Apply("MediaListCollection", ReplayRead(reread));

        var lists = reread["data"]?["MediaListCollection"]?["lists"]?.AsArray() ?? [];
        Assert.That(lists[expectedList]?.ToJsonString(), Does.Contain($"\"mediaId\":{mediaId}"),
            $"The title moved to {status} is not under that section.");
        Assert.That(lists[1 - expectedList]?.ToJsonString(), Does.Not.Contain($"\"mediaId\":{mediaId}"));
    }

    [Test]
    public void ATitleAddedFromItsRelations_CarriesTheMediaTheRelationsShowed()
    {
        // #221's second gap: Up Next adds a related title, and with no recorded Media for it the
        // saved entry had no media block, so the Library drew a blank card. The relations read that
        // offered it already carried its title, cover and format.
        var state = new MutationState();
        state.Apply("MediaRelations", Json("""
            {"data":{"Media":{"id":11757,"relations":{"edges":[
              {"relationType":"SEQUEL","node":{"id":20021,"title":{"romaji":"Sword Art Online: Extra Edition"},"format":"SPECIAL","type":"ANIME","episodes":1}}
            ]}}}}
            """));
        state.Apply("MediaListCollection", TwoSectionList());

        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":20021,"status":"CURRENT"}"""), NoFixtures, out _);

        var reread = TwoSectionList();
        state.Apply("MediaListCollection", reread);

        var added = (reread["data"]?["MediaListCollection"]?["lists"]?[0]?["entries"]?.AsArray() ?? [])
            .SingleOrDefault(e => e?["mediaId"]?.GetValue<int>() == 20021);
        Assert.That(added?["media"]?["title"]?["romaji"]?.GetValue<string>(), Is.EqualTo("Sword Art Online: Extra Edition"));
    }

    // ── What a details read shows afterwards ─────────────────────────

    [Test]
    public void AStatusChange_ShowsOnTheNextDetailsRead()
    {
        // Found by the #225 journey: pause a title on its details page, go back, open it again, and
        // the page said Watching, because the recorded Media read carries the entry as recorded. The
        // Library showed it under Paused, so the two pages disagreed about one title.
        var state = new MutationState();
        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":21,"status":"PAUSED","progress":1163}"""), NoFixtures, out _);

        var media = MediaResponse(mediaId: 21);
        state.Apply("Media", media);

        var entry = media["data"]?["Media"]?["mediaListEntry"];
        Assert.That(entry?["status"]?.GetValue<string>(), Is.EqualTo("PAUSED"));
        Assert.That(entry?["progress"]?.GetValue<int>(), Is.EqualTo(1163));
        Assert.That(entry?["id"]?.GetValue<int>(), Is.EqualTo(900_000_021), "the id the save answered with");
        Assert.That(entry?["media"], Is.Null, "a details read's entry carries no media block of its own");
    }

    [Test]
    public void ATitleAddedDuringReplay_ShowsItsEntryOnTheNextDetailsRead()
    {
        // The recording has no entry at all for a title the account never listed.
        var state = new MutationState();
        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":21,"status":"PLANNING"}"""), NoFixtures, out _);

        var media = MediaResponse(mediaId: 21, listed: false);
        state.Apply("Media", media);

        Assert.That(media["data"]?["Media"]?["mediaListEntry"]?["status"]?.GetValue<string>(), Is.EqualTo("PLANNING"));
    }

    [Test]
    public void ARemovedEntry_IsGoneFromTheNextDetailsRead()
    {
        var state = new MutationState();
        state.Apply("MediaListCollection", ListResponse());
        state.TryAnswer("DeleteMediaListEntry", Vars("""{"id":1001}"""), NoFixtures, out _);

        var media = MediaResponse(mediaId: 21);
        state.Apply("Media", media);

        Assert.That(media["data"]?["Media"]?["mediaListEntry"], Is.Null);
    }

    [Test]
    public void AnotherTitlesDetailsRead_IsLeftAsRecorded()
    {
        var state = new MutationState();
        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":21,"status":"PAUSED"}"""), NoFixtures, out _);

        var media = MediaResponse(mediaId: 16498);
        state.Apply("Media", media);

        Assert.That(media["data"]?["Media"]?["mediaListEntry"]?["status"]?.GetValue<string>(), Is.EqualTo("CURRENT"));
        Assert.That(media["data"]?["Media"]?["mediaListEntry"]?["id"]?.GetValue<int>(), Is.EqualTo(1002));
    }

    [Test]
    public void ADetailsRead_TeachesTheEntryId_SoARemovalMadeFromItSticks()
    {
        // Copilot on #233: opened from Search before any list read, the entry id was unknown, so the
        // removal changed nothing and the next details read showed the title still listed.
        var state = new MutationState();
        state.Apply("Media", MediaResponse(mediaId: 21));

        state.TryAnswer("DeleteMediaListEntry", Vars("""{"id":1001}"""), NoFixtures, out _);

        var reread = MediaResponse(mediaId: 21);
        state.Apply("Media", reread);
        Assert.That(reread["data"]?["Media"]?["mediaListEntry"], Is.Null);
    }

    [Test]
    public void ABrowseRead_TeachesTheEntryId_AndShowsTheRemoval()
    {
        // Search, Discover, View All, relations and the people pages all carry the viewer's entry on
        // each card; the same gap was in every one of them.
        var state = new MutationState();
        state.Apply("Search", SearchResponse());

        state.TryAnswer("DeleteMediaListEntry", Vars("""{"id":1001}"""), NoFixtures, out _);

        var reread = SearchResponse();
        state.Apply("Search", reread);
        Assert.That(CardEntry(reread, 21), Is.Null);
        Assert.That(CardEntry(reread, 16498)?["status"]?.GetValue<string>(), Is.EqualTo("CURRENT"));
    }

    [Test]
    public void ABrowseRead_ShowsASave_InTheShapeThatQuerySelects()
    {
        var state = new MutationState();
        state.TryAnswer(
            "SaveMediaListEntry",
            Vars("""{"mediaId":21,"status":"PAUSED","progress":9,"progressVolumes":2,"notes":"later"}"""),
            NoFixtures,
            out _);

        var reread = SearchResponse();
        state.Apply("Search", reread);

        var entry = CardEntry(reread, 21) as JsonObject;
        Assert.That(entry?["status"]?.GetValue<string>(), Is.EqualTo("PAUSED"));
        Assert.That(entry?["progress"]?.GetValue<int>(), Is.EqualTo(9));
        Assert.That(entry?.Select(p => p.Key), Is.EquivalentTo(new[] { "id", "status", "progress", "score" }));
    }

    [Test]
    public void ABrowseRead_OfATitleTheRecordingHadOffTheList_ShowsTheSaveInTheCardShape()
    {
        var state = new MutationState();
        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":5114,"status":"PLANNING","notes":"x"}"""), NoFixtures, out _);

        var reread = SearchResponse();
        state.Apply("Search", reread);

        var entry = CardEntry(reread, 5114) as JsonObject;
        Assert.That(entry?["status"]?.GetValue<string>(), Is.EqualTo("PLANNING"));
        Assert.That(entry?.Select(p => p.Key), Is.EquivalentTo(new[] { "id", "status", "progress", "score" }));
    }

    [Test]
    public void ADetailsRead_OfATitleTheRecordingHadOffTheList_SelectsWhatMediaQuerySelects()
    {
        // Copilot on #233: replay showed whatever the save carried rather than what the details query
        // selects, so a field a live read could not return looked present.
        var state = new MutationState();
        state.TryAnswer(
            "SaveMediaListEntry", Vars("""{"mediaId":21,"status":"PAUSED","progressVolumes":2}"""), NoFixtures, out _);

        var media = MediaResponse(mediaId: 21, listed: false);
        state.Apply("Media", media);

        var entry = media["data"]?["Media"]?["mediaListEntry"] as JsonObject;
        Assert.That(entry?.Select(p => p.Key), Is.EquivalentTo(new[]
        {
            "id", "mediaId", "status", "progress", "progressVolumes", "score", "repeat", "notes", "private", "hiddenFromStatusLists", "updatedAt",
        }));
    }

    [Test]
    public void ASaveThatLeavesAFieldNull_KeepsWhatTheRecordingHad()
    {
        // Copilot on #233: AniList keeps a field the save sends as null; replay blanked it.
        var state = new MutationState();
        state.TryAnswer("SaveMediaListEntry", Vars("""{"mediaId":21,"status":"PAUSED"}"""), NoFixtures, out _);

        var media = MediaResponse(mediaId: 21);
        state.Apply("Media", media);

        var entry = media["data"]?["Media"]?["mediaListEntry"];
        Assert.That(entry?["status"]?.GetValue<string>(), Is.EqualTo("PAUSED"));
        Assert.That(entry?["progress"]?.GetValue<int>(), Is.EqualTo(3));
        Assert.That(entry?["score"]?.GetValue<int>(), Is.EqualTo(8));
    }

    [Test]
    public void ASaveThatLeavesAFieldNull_KeepsItInTheSaveAnswerAndTheNextListReadToo()
    {
        // The app adopts the save's answer as its entry, so a blank there is as wrong as one on the
        // next read. The id stays the one AniList already had: it mints a new one only for a new entry.
        var state = new MutationState();
        state.Apply("MediaListCollection", Json("""
            {"data":{"MediaListCollection":{"lists":[{"name":"Watching","entries":[
              {"id":1001,"mediaId":21,"status":"CURRENT","progress":3,"notes":"with friends","media":{"id":21}}
            ]},{"name":"Paused","entries":[
              {"id":1004,"mediaId":30,"status":"PAUSED","progress":1,"notes":null,"media":{"id":30}}
            ]}]}}}
            """));

        state.TryAnswer("SaveMediaListEntry", Vars("""{"mediaId":21,"status":"PAUSED"}"""), NoFixtures, out var answer);

        var saved = answer?["data"]?["SaveMediaListEntry"];
        Assert.That(saved?["id"]?.GetValue<int>(), Is.EqualTo(1001));
        Assert.That(saved?["notes"]?.GetValue<string>(), Is.EqualTo("with friends"));
        Assert.That(saved?["progress"]?.GetValue<int>(), Is.EqualTo(3));

        var reread = Json("""
            {"data":{"MediaListCollection":{"lists":[{"name":"Watching","entries":[
              {"id":1001,"mediaId":21,"status":"CURRENT","progress":3,"notes":"with friends","media":{"id":21}}
            ]},{"name":"Paused","entries":[
              {"id":1004,"mediaId":30,"status":"PAUSED","progress":1,"notes":null,"media":{"id":30}}
            ]}]}}}
            """);
        state.Apply("MediaListCollection", reread);
        var moved = (reread["data"]?["MediaListCollection"]?["lists"]?[1]?["entries"]?.AsArray() ?? [])
            .SingleOrDefault(e => e?["mediaId"]?.GetValue<int>() == 21);
        Assert.That(moved?["notes"]?.GetValue<string>(), Is.EqualTo("with friends"));
        Assert.That(moved?["id"]?.GetValue<int>(), Is.EqualTo(1001));

        // In the list query's shape, with the list's own media block: nothing that query does not
        // select, and not a Media read's block, which is shaped differently.
        Assert.That((moved as JsonObject)?.ContainsKey("hiddenFromStatusLists"), Is.False);
        Assert.That(moved?["media"]?["id"]?.GetValue<int>(), Is.EqualTo(21));
    }

    /// <summary>A recorded <c>Search</c> page: title 21 on Watching, 16498 on Watching, 5114 off the list.</summary>
    private static JsonNode SearchResponse() => Json("""
        {"data":{"Page":{"pageInfo":{"hasNextPage":false},"media":[
          {"id":21,"title":{"romaji":"ONE PIECE"},"mediaListEntry":{"id":1001,"status":"CURRENT","progress":3,"score":8}},
          {"id":16498,"title":{"romaji":"Shingeki no Kyojin"},"mediaListEntry":{"id":1002,"status":"CURRENT","progress":1,"score":0}},
          {"id":5114,"title":{"romaji":"Hagane"},"mediaListEntry":null}
        ]}}}
        """);

    private static JsonNode? CardEntry(JsonNode search, int mediaId)
        => (search["data"]?["Page"]?["media"]?.AsArray() ?? [])
            .Single(m => m?["id"]?.GetValue<int>() == mediaId)?["mediaListEntry"];

    /// <summary>A recorded <c>Media</c> read, the title on Watching as the recording found it.</summary>
    private static JsonNode MediaResponse(int mediaId, bool listed = true) => new JsonObject
    {
        ["data"] = new JsonObject
        {
            ["Media"] = new JsonObject
            {
                ["id"] = mediaId,
                ["title"] = new JsonObject { ["romaji"] = $"Title {mediaId}" },
                ["mediaListEntry"] = !listed ? null : new JsonObject
                {
                    ["id"] = mediaId == 21 ? 1001 : 1002,
                    ["status"] = "CURRENT",
                    ["progress"] = 3,
                    ["score"] = 8,
                },
            },
        },
    };

    private static JsonNode ReplayRead(JsonNode list)
    {
        AdultCanary.Splice("MediaListCollection", Vars("""{"type":"ANIME"}"""), list);
        return list;
    }

    /// <summary>The test account's order: Completed comes first, so the canary rides it.</summary>
    private static JsonNode Json(string json)
        => JsonNode.Parse(json) ?? throw new ArgumentException("Not a JSON value.", nameof(json));

    private static JsonNode CompletedFirstList() => Json("""
        {"data":{"MediaListCollection":{"lists":[
          {"name":"Completed","entries":[
            {"id":1003,"mediaId":5114,"status":"COMPLETED"}
          ]},
          {"name":"Watching","entries":[
            {"id":1001,"mediaId":21,"status":"CURRENT"},
            {"id":1002,"mediaId":16498,"status":"CURRENT"}
          ]}
        ]}}}
        """);

    // ── Settings round-trip ──────────────────────────────────────────

    [Test]
    public void UpdateUser_SurvivesTheNextViewerRead()
    {
        // Settings reloads from ViewerFull, so a preference change that did not survive it reverted
        // on the next visit — which made the whole Display Preferences section unverifiable in CI
        // before (#130).
        var state = new MutationState();
        var fixtures = new StubLookup(GraphQlFixtureKey.Derive("ViewerFull", null), ViewerFixture());

        state.TryAnswer("UpdateUser", Vars("""{"titleLanguage":"NATIVE"}"""), fixtures, out var updated);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated["data"]!["UpdateUser"]!["options"]!["titleLanguage"]!.GetValue<string>(), Is.EqualTo("NATIVE"));

        var reread = ViewerResponse();
        state.Apply("ViewerFull", reread);
        Assert.That(reread["data"]!["Viewer"]!["options"]!["titleLanguage"]!.GetValue<string>(), Is.EqualTo("NATIVE"));
    }

    [Test]
    public void UpdateUser_RefusesToTurnOnAdultContent()
    {
        // The canary gate depends on the viewer reporting adult content off. If the app could flip it,
        // a CI run could disarm its own safety check — so this one variable is deliberately ignored.
        var state = new MutationState();
        var fixtures = new StubLookup(GraphQlFixtureKey.Derive("ViewerFull", null), ViewerFixture());

        state.TryAnswer("UpdateUser", Vars("""{"displayAdultContent":true}"""), fixtures, out var updated);

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated["data"]!["UpdateUser"]!["options"]!["displayAdultContent"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public void ScoreFormat_LandsOnMediaListOptionsNotOptions()
    {
        // The one UpdateUser argument that does not live under `options` on the viewer. Writing it to
        // the wrong place leaves the Settings picker showing the old format after a save.
        var state = new MutationState();
        var fixtures = new StubLookup(GraphQlFixtureKey.Derive("ViewerFull", null), ViewerFixture());

        state.TryAnswer("UpdateUser", Vars("""{"scoreFormat":"POINT_5"}"""), fixtures, out var updated);

        Assert.That(updated, Is.Not.Null);
        var user = updated["data"]!["UpdateUser"]!;
        Assert.That(user["mediaListOptions"]!["scoreFormat"]!.GetValue<string>(), Is.EqualTo("POINT_5"));
        Assert.That(user["options"]!["scoreFormat"], Is.Null);
    }

    // ── Dispatch ─────────────────────────────────────────────────────

    [Test]
    public void AReadOperation_IsNotAnsweredHere()
    {
        // Reads must fall through to the recorded fixtures. Answering one here would shadow real data
        // with a synthesized reply.
        var state = new MutationState();

        Assert.That(state.TryAnswer("MediaListCollection", Vars("{}"), NoFixtures, out _), Is.False);
    }

    [Test]
    public void ToggleFavourite_ReportsSuccess()
    {
        var state = new MutationState();

        Assert.That(state.TryAnswer("ToggleFavourite", Vars("""{"animeId":21}"""), NoFixtures, out var response), Is.True);
        Assert.That(response, Is.Not.Null);
        Assert.That(response["data"]!["ToggleFavourite"], Is.Not.Null);
    }

    private static IFixtureLookup NoFixtures => new StubLookup(key: null, fixture: null);

    private static JsonNode? Vars(string json) => JsonNode.Parse(json);

    /// <summary>Two sections with distinct statuses, so a move between them is observable.</summary>
    private static JsonNode TwoSectionList() => JsonNode.Parse("""
        {"data":{"MediaListCollection":{"lists":[
          {"name":"Watching","entries":[
            {"id":1001,"mediaId":21,"status":"CURRENT"},
            {"id":1002,"mediaId":16498,"status":"CURRENT"}
          ]},
          {"name":"Completed","entries":[
            {"id":1003,"mediaId":5114,"status":"COMPLETED"}
          ]}
        ]}}}
        """)!;

    /// <summary>
    /// Entries carry <c>status</c> because real <c>MediaListCollection</c> responses do —
    /// <c>MediaListQuery</c> selects it. Omitting it here made a save look unreplayable when the
    /// only thing missing was the field the fixture should have had.
    /// </summary>
    private static JsonNode ListResponse() => JsonNode.Parse("""
        {"data":{"MediaListCollection":{"lists":[{"name":"Watching","entries":[
          {"id":1001,"mediaId":21,"status":"CURRENT"},
          {"id":1002,"mediaId":16498,"status":"CURRENT"}
        ]}]}}}
        """)!;

    private static JsonNode ViewerResponse() => JsonNode.Parse("""
        {"data":{"Viewer":{"id":7,"name":"tester",
          "options":{"titleLanguage":"ROMAJI","displayAdultContent":false},
          "mediaListOptions":{"scoreFormat":"POINT_10"}}}}
        """)!;

    private static GraphQlFixture ViewerFixture() => new()
    {
        OperationName = "ViewerFull",
        QueryFingerprint = "test",
        Response = ViewerResponse(),
    };

    /// <summary>One canned recording, or none. Enough for the two lookups mutations make.</summary>
    private sealed class StubLookup(string? key, GraphQlFixture? fixture) : IFixtureLookup
    {
        public bool TryGet(
            string lookupKey, string? queryFingerprint, [NotNullWhen(true)] out GraphQlFixture? found)
        {
            found = string.Equals(lookupKey, key, StringComparison.Ordinal) ? fixture : null;
            return found is not null;
        }
    }
}
