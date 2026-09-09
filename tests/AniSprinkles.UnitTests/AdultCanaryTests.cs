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

    [Fact]
    public void TheList_AlwaysCarriesTheCanary()
    {
        // MediaListCollection takes no isAdult argument — AniList cannot filter it server-side, so
        // the client-side filter in MediaListSectionsMerger is the only thing standing between an
        // 18+ entry and the Library. That filter is only exercised if the canary is always present.
        var response = ListResponse();

        AdultCanary.Splice("MediaListCollection", variables: null, response);

        Assert.Contains(AdultCanary.Title, response.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheListCanary_IsFlaggedAdultSoTheFilterCanSeeIt()
    {
        var response = ListResponse();

        AdultCanary.Splice("MediaListCollection", variables: null, response);

        var entry = response["data"]!["MediaListCollection"]!["lists"]![0]!["entries"]![0];
        Assert.True(entry!["media"]!["isAdult"]!.GetValue<bool>());
    }

    // ── The browse half: armed only when the filter is not pinned ────

    [Fact]
    public void BrowsePinnedToSfw_DoesNotGetTheCanary()
    {
        // A correct app always pins isAdult:false while the viewer has adult content off. Serving the
        // canary here would fail every CI run for a filter that is working.
        var response = MediaArrayResponse();

        AdultCanary.Splice("BrowseAnime", Vars("""{"isAdult":false}"""), response);

        Assert.DoesNotContain(AdultCanary.Title, response.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseWithTheFilterOmitted_GetsTheCanary()
    {
        // The failure this catches: an omitted argument matches everything, so a dropped isAdult is
        // indistinguishable from "show me adult content". That is the regression worth a red build.
        var response = MediaArrayResponse();

        AdultCanary.Splice("BrowseAnime", Vars("{}"), response);

        Assert.Contains(AdultCanary.Title, response.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseAskingForAdultOnly_GetsTheCanary()
    {
        var response = MediaArrayResponse();

        AdultCanary.Splice("BrowseAnime", Vars("""{"isAdult":true}"""), response);

        Assert.Contains(AdultCanary.Title, response.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
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
            Assert.Equal(AdultCanary.Title, media[0]!["title"]!["romaji"]!.GetValue<string>());
        }
    }

    // ── The canary itself ────────────────────────────────────────────

    [Fact]
    public void TheCanary_HasNoCoverImage()
    {
        // It is a marker, not content. Nothing should be fetched or rendered if the filter fails —
        // the shouted title next to the app's own placeholder is the entire payload.
        var response = MediaArrayResponse();

        AdultCanary.Splice("Search", Vars("{}"), response);

        var canary = response["data"]!["Page"]!["media"]!.AsArray()[0];
        Assert.Null(canary!["coverImage"]);
    }

    [Fact]
    public void TheTitle_IsTheStringTheWorkflowGrepsFor()
    {
        // ci-build-and-preview.yml greps the UI dump for this literal and asserts it still appears in
        // AdultCanary.cs. Renaming it without updating the workflow leaves the gate matching nothing.
        Assert.Equal("18PLUS CANARY - FILTER FAILED", AdultCanary.Title);
    }

    private static JsonNode ListResponse() => JsonNode.Parse("""
        {"data":{"MediaListCollection":{"lists":[{"name":"Watching","entries":[{"id":1,"mediaId":21}]}]}}}
        """)!;

    private static JsonNode MediaArrayResponse() => JsonNode.Parse("""
        {"data":{"Page":{"media":[{"id":21,"title":{"romaji":"ONE PIECE"}}]}}}
        """)!;

    private static JsonNode? Vars(string json) => JsonNode.Parse(json);
}
