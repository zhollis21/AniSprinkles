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

    [Fact]
    public void ADeletedEntry_DisappearsFromTheNextListRead()
    {
        // The lived failure this prevents: tap "remove from list", navigate back to the Library, and
        // the entry is still there because the recorded list never heard about the delete. The app
        // looks broken for a reason that has nothing to do with the app.
        var state = new MutationState();
        var list = ListResponse();
        state.Apply("MediaListCollection", list);

        Assert.True(state.TryAnswer("DeleteMediaListEntry", Vars("""{"id":1001}"""), NoFixtures, out _));

        var reread = ListResponse();
        state.Apply("MediaListCollection", reread);

        Assert.DoesNotContain("\"mediaId\":21", reread.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("\"mediaId\":16498", reread.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
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

        Assert.Contains("\"mediaId\":21", reread.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeletingBeforeAnyListRead_ChangesNothing()
    {
        // Without a read there is no entry-id-to-media-id translation, and nothing rendered that the
        // delete could be inconsistent with. Guessing would be worse than doing nothing.
        var state = new MutationState();

        Assert.True(state.TryAnswer("DeleteMediaListEntry", Vars("""{"id":1001}"""), NoFixtures, out var response));
        Assert.True(response["data"]!["DeleteMediaListEntry"]!["deleted"]!.GetValue<bool>());

        var list = ListResponse();
        state.Apply("MediaListCollection", list);
        Assert.Contains("\"mediaId\":21", list.ToJsonString(), StringComparison.Ordinal);
    }

    // ── Settings round-trip ──────────────────────────────────────────

    [Fact]
    public void UpdateUser_SurvivesTheNextViewerRead()
    {
        // Settings reloads from ViewerFull, so a preference change that did not survive it reverted
        // on the next visit — which made the whole Display Preferences section unverifiable in CI
        // before (#130).
        var state = new MutationState();
        var fixtures = new StubLookup(GraphQlFixtureKey.Derive("ViewerFull", null), ViewerFixture());

        state.TryAnswer("UpdateUser", Vars("""{"titleLanguage":"NATIVE"}"""), fixtures, out var updated);
        Assert.NotNull(updated);
        Assert.Equal("NATIVE", updated["data"]!["UpdateUser"]!["options"]!["titleLanguage"]!.GetValue<string>());

        var reread = ViewerResponse();
        state.Apply("ViewerFull", reread);
        Assert.Equal("NATIVE", reread["data"]!["Viewer"]!["options"]!["titleLanguage"]!.GetValue<string>());
    }

    [Fact]
    public void UpdateUser_RefusesToTurnOnAdultContent()
    {
        // The canary gate depends on the viewer reporting adult content off. If the app could flip it,
        // a CI run could disarm its own safety check — so this one variable is deliberately ignored.
        var state = new MutationState();
        var fixtures = new StubLookup(GraphQlFixtureKey.Derive("ViewerFull", null), ViewerFixture());

        state.TryAnswer("UpdateUser", Vars("""{"displayAdultContent":true}"""), fixtures, out var updated);

        Assert.NotNull(updated);
        Assert.False(updated["data"]!["UpdateUser"]!["options"]!["displayAdultContent"]!.GetValue<bool>());
    }

    [Fact]
    public void ScoreFormat_LandsOnMediaListOptionsNotOptions()
    {
        // The one UpdateUser argument that does not live under `options` on the viewer. Writing it to
        // the wrong place leaves the Settings picker showing the old format after a save.
        var state = new MutationState();
        var fixtures = new StubLookup(GraphQlFixtureKey.Derive("ViewerFull", null), ViewerFixture());

        state.TryAnswer("UpdateUser", Vars("""{"scoreFormat":"POINT_5"}"""), fixtures, out var updated);

        Assert.NotNull(updated);
        var user = updated["data"]!["UpdateUser"]!;
        Assert.Equal("POINT_5", user["mediaListOptions"]!["scoreFormat"]!.GetValue<string>());
        Assert.Null(user["options"]!["scoreFormat"]);
    }

    // ── Dispatch ─────────────────────────────────────────────────────

    [Fact]
    public void AReadOperation_IsNotAnsweredHere()
    {
        // Reads must fall through to the recorded fixtures. Answering one here would shadow real data
        // with a synthesized reply.
        var state = new MutationState();

        Assert.False(state.TryAnswer("MediaListCollection", Vars("{}"), NoFixtures, out _));
    }

    [Fact]
    public void ToggleFavourite_ReportsSuccess()
    {
        var state = new MutationState();

        Assert.True(state.TryAnswer("ToggleFavourite", Vars("""{"animeId":21}"""), NoFixtures, out var response));
        Assert.NotNull(response["data"]!["ToggleFavourite"]);
    }

    private static IFixtureLookup NoFixtures => new StubLookup(key: null, fixture: null);

    private static JsonNode? Vars(string json) => JsonNode.Parse(json);

    private static JsonNode ListResponse() => JsonNode.Parse("""
        {"data":{"MediaListCollection":{"lists":[{"name":"Watching","entries":[
          {"id":1001,"mediaId":21},
          {"id":1002,"mediaId":16498}
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
