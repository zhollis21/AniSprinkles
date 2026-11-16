using System.Text.Json.Nodes;
using AniSprinkles.Services.Fixtures;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The adult-content canary (#118), as it works against replayed fixtures (#134).
/// <para>
/// This is the highest-stakes logic in the fixture stack and the least visible: it decides whether
/// CI can detect an adult-filter regression at all. If the canary stops being served, the gate goes
/// green forever while the thing it guards is broken — the exact failure #122 exists to prevent, one
/// layer down. So the arming rules are pinned here rather than trusted.
/// </para>
/// </summary>
public class AdultCanaryTests
{
    // ── The list half: always armed ──────────────────────────────────

    [Test]
    public void TheList_AlwaysCarriesTheCanary()
    {
        // MediaListCollection takes no isAdult argument — AniList cannot filter it server-side, so
        // the client-side filter in MediaListSectionsMerger is the only thing standing between an
        // 18+ entry and the Library. That filter is only exercised if the canary is always present.
        var response = ListResponse();

        AdultCanary.Splice("MediaListCollection", variables: null, response);

        Assert.That(response.ToJsonString(), Does.Contain(AdultCanary.Title));
    }

    [Test]
    public void TheListCanary_IsFlaggedAdultSoTheFilterCanSeeIt()
    {
        var response = ListResponse();

        AdultCanary.Splice("MediaListCollection", variables: null, response);

        var entry = response["data"]!["MediaListCollection"]!["lists"]![0]!["entries"]![0];
        Assert.That(entry!["media"]!["isAdult"]!.GetValue<bool>(), Is.True);
    }

    // ── The browse half: armed only when the filter is not pinned ────

    [Test]
    public void BrowsePinnedToSfw_DoesNotGetTheCanary()
    {
        // A correct app always pins isAdult:false while the viewer has adult content off. Serving the
        // canary here would fail every CI run for a filter that is working.
        var response = MediaArrayResponse();

        AdultCanary.Splice("BrowseAnime", Vars("""{"isAdult":false}"""), response);

        // The marker, not just the canary's title: none of the made-up titles belongs here either.
        Assert.That(response.ToJsonString(), Does.Not.Contain(AdultCanary.Marker));
    }

    [Test]
    public void BrowseWithTheFilterOmitted_GetsTheCanary()
    {
        // The failure this catches: an omitted argument matches everything, so a dropped isAdult is
        // indistinguishable from "show me adult content". That is the regression worth a red build.
        var response = MediaArrayResponse();

        AdultCanary.Splice("BrowseAnime", Vars("{}"), response);

        Assert.That(response.ToJsonString(), Does.Contain(AdultCanary.Title));
    }

    [Test]
    public void BrowseAskingForAdultOnly_GetsTheCanary()
    {
        var response = MediaArrayResponse();

        AdultCanary.Splice("BrowseAnime", Vars("""{"isAdult":true}"""), response);

        Assert.That(response.ToJsonString(), Does.Contain(AdultCanary.Title));
    }

    [Test]
    public void EveryDiscoverSection_IsCovered()
    {
        // Discover returns several aliased pages in one response. Covering only the first would leave
        // the rest unguarded, and the aliases change as sections are added.
        var response = JsonNode.Parse("""
            {"data":{
              "airing":{"media":[{"id":1}]},
              "trending":{"media":[{"id":2}]},
              "top":{"media":[{"id":3}]}
            }}
            """)!;

        AdultCanary.Splice("DiscoverSections", Vars("{}"), response);

        foreach (var alias in new[] { "airing", "trending", "top" })
        {
            var media = response["data"]![alias]!["media"]!.AsArray();
            Assert.That(media[0]!["title"]!["romaji"]!.GetValue<string>(), Is.EqualTo(AdultCanary.Title));
        }
    }

    // ── The canary itself ────────────────────────────────────────────

    [Test]
    public void TheCanary_HasNoCoverImage()
    {
        // It is a marker, not content. Nothing should be fetched or rendered if the filter fails —
        // the shouted title next to the app's own placeholder is the entire payload.
        var response = MediaArrayResponse();

        AdultCanary.Splice("Search", Vars("{}"), response);

        var canary = response["data"]!["Page"]!["media"]!.AsArray()[0];
        Assert.That(canary!["coverImage"], Is.Null);
    }

    [Test]
    public void TheTitle_IsTheStringTheWorkflowGrepsFor()
    {
        // ci-build-and-preview.yml greps the UI dump for this literal and asserts it still appears in
        // AdultCanary.cs. Renaming it without updating the workflow leaves the gate matching nothing.
        Assert.That(AdultCanary.Title, Is.EqualTo("18PLUS CANARY - FILTER FAILED"));
    }

    // ── The made-up 18+ titles ───────────────────────────────────────
    //
    // The recorder now keeps every real Ecchi and 18+ title out of the fixtures, so these are the
    // only adult content the CI build has. Without them the 18+ Discover rows and an adult-on Library
    // would be empty, with nothing to test against.

    [Test]
    public void EveryMadeUpTitle_CarriesTheMarkerTheWorkflowGrepsFor()
    {
        // They are served wherever the canary is, so a broken filter that lets one through has to
        // fail the same gate. ci-build-and-preview.yml greps for "18PLUS CANARY".
        Assert.That(AdultCanary.MadeUpTitles, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(AdultCanary.MadeUpTitles, Has.All.StartsWith("18PLUS CANARY"));
    }

    [Test]
    public void TheMangaList_GetsTheMadeUpManga_AndTheAnimeListDoesNot()
    {
        var anime = ListResponse();
        var manga = ListResponse();

        AdultCanary.Splice("MediaListCollection", Vars("""{"type":"ANIME"}"""), anime);
        AdultCanary.Splice("MediaListCollection", Vars("""{"type":"MANGA"}"""), manga);

        Assert.That(TypesIn(manga), Does.Contain("MANGA"));
        Assert.That(TypesIn(anime), Does.Not.Contain("MANGA"));
    }

    [Test]
    public void TheListCanary_StaysArmedOnBothLists()
        // Typing the made-up titles must not cost the manga list its canary.
        => Assert.That(
            Splice("MediaListCollection", """{"type":"MANGA"}""", ListResponse()),
            Does.Contain(AdultCanary.Title));

    [Test]
    public void BrowseWithTheFilterOmitted_GetsOnlyAnime()
    {
        // BrowseAnime is anime by definition, and an anime row with a manga in it would be a bug of
        // its own.
        var response = MediaArrayResponse();

        AdultCanary.Splice("BrowseAnime", Vars("{}"), response);

        var types = response["data"]!["Page"]!["media"]!.AsArray()
            .Select(m => m!["type"]?.GetValue<string>())
            .Where(t => t is not null);
        Assert.That(types, Has.All.EqualTo("ANIME"));
    }

    [Test]
    public void TheDetailsOfAMadeUpTitle_AreAnswered()
    {
        // Tapping one opens its details page. Without an answer that is a fixture miss, which fails
        // loudly, so adult-on testing would stop at the first tap.
        var id = MadeUpId("MediaListCollection", """{"type":"ANIME"}""");

        Assert.That(AdultCanary.TryAnswer("Media", Vars($$"""{"id":{{id}}}"""), out var response), Is.True);

        var media = response!["data"]!["Media"]!;
        Assert.That(media["isAdult"]!.GetValue<bool>(), Is.True);
        Assert.That(media["coverImage"], Is.Null);
        Assert.That(media["title"]!["romaji"]!.GetValue<string>(), Does.StartWith("18PLUS CANARY"));
    }

    [Test]
    public void SearchWithoutAType_GetsBothAnimeAndManga()
    {
        // Search is the one surface that spans both types, so an untyped search is where the
        // made-up manga can be found at all outside the manga list.
        Assert.That(TypesInPage("Search", "{}"), Is.SupersetOf(new[] { "ANIME", "MANGA" }));
    }

    [Test]
    public void SearchForManga_GetsTheMadeUpMangaAndOnlyTheCanaryAnime()
    {
        var names = NamesInPage("Search", """{"type":"MANGA"}""");

        Assert.That(names, Does.Contain(AdultCanary.Title));
        Assert.That(names, Has.Some.Contains("Velvet Sprinkles"));
        Assert.That(names, Has.None.Contains("Midnight Parfait"));
    }

    [Test]
    public void TheDetailsOfARealTitle_AreLeftToTheFixtures()
        => Assert.That(AdultCanary.TryAnswer("Media", Vars("""{"id":21}"""), out _), Is.False);

    [Test]
    public void AnotherOperationWithAMadeUpId_IsLeftToTheFixtures()
    {
        // Character and Staff ids come from their own number ranges. Only a Media request is about
        // a made-up title.
        var id = MadeUpId("MediaListCollection", """{"type":"ANIME"}""");

        Assert.That(AdultCanary.TryAnswer("Character", Vars($$"""{"id":{{id}}}"""), out _), Is.False);
    }

    [Test]
    public void AMediaRequestWithNoId_IsLeftToTheFixtures()
        => Assert.That(AdultCanary.TryAnswer("Media", Vars("{}"), out _), Is.False);

    private static List<string?> TypesInPage(string operationName, string variables)
        => PageMedia(operationName, variables).Select(m => m?["type"]?.GetValue<string>()).ToList();

    private static List<string> NamesInPage(string operationName, string variables)
        => [.. PageMedia(operationName, variables)
            .Select(m => m?["title"]?["romaji"]?.GetValue<string>())
            .OfType<string>()];

    private static JsonArray PageMedia(string operationName, string variables)
    {
        var response = MediaArrayResponse();
        AdultCanary.Splice(operationName, Vars(variables), response);
        return response["data"]!["Page"]!["media"]!.AsArray();
    }

    private static string Splice(string operationName, string variables, JsonNode response)
    {
        AdultCanary.Splice(operationName, Vars(variables), response);
        return response.ToJsonString();
    }

    private static List<string?> TypesIn(JsonNode listResponse)
        => listResponse["data"]!["MediaListCollection"]!["lists"]![0]!["entries"]!.AsArray()
            .Select(e => e!["media"]?["type"]?.GetValue<string>())
            .ToList();

    /// <summary>The id of a made-up title other than the canary, as the list serves it.</summary>
    private static int MadeUpId(string operationName, string variables)
    {
        var response = ListResponse();
        AdultCanary.Splice(operationName, Vars(variables), response);
        return response["data"]!["MediaListCollection"]!["lists"]![0]!["entries"]!.AsArray()
            .Select(e => e!["media"])
            .Where(m => m?["title"]?["romaji"]?.GetValue<string>() is { } t
                && t.StartsWith("18PLUS CANARY", StringComparison.Ordinal)
                && t != AdultCanary.Title)
            .Select(m => m!["id"]!.GetValue<int>())
            .First();
    }

    private static JsonNode ListResponse() => JsonNode.Parse("""
        {"data":{"MediaListCollection":{"lists":[{"name":"Watching","entries":[{"id":1,"mediaId":21}]}]}}}
        """)!;

    private static JsonNode MediaArrayResponse() => JsonNode.Parse("""
        {"data":{"Page":{"media":[{"id":21,"title":{"romaji":"ONE PIECE"}}]}}}
        """)!;

    private static JsonNode? Vars(string json) => JsonNode.Parse(json);
}
