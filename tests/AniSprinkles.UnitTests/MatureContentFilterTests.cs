using System.Text.Json.Nodes;
using AniSprinkles.Services.Fixtures;

namespace AniSprinkles.UnitTests;

/// <summary>
/// What keeps Ecchi and 18+ titles out of the recorded fixtures. Those files are committed to a
/// public repository and rendered into CI screenshots, so a title slipping through is not a test
/// failure anyone would notice — it is just there, the way The Qwaser of Stigmata II was.
/// </summary>
public class MatureContentFilterTests
{
    // ── Which titles ─────────────────────────────────────────────────

    [Test]
    public void AnAdultTitle_IsExcluded()
        => Assert.That(MatureContentFilter.IsExcluded(isAdult: true, genres: ["Romance"]), Is.True);

    [Test]
    public void AnEcchiTitle_IsExcluded_EvenThoughAniListDoesNotFlagItAdult()
        // Ecchi is SFW as far as AniList's isAdult is concerned, which is exactly how one got onto
        // the test account's list and into the fixtures with its cover intact.
        => Assert.That(MatureContentFilter.IsExcluded(isAdult: false, genres: ["Action", "Ecchi"]), Is.True);

    [Test]
    public void AnOrdinaryTitle_IsKept()
        => Assert.That(MatureContentFilter.IsExcluded(isAdult: false, genres: ["Action", "Adventure"]), Is.False);

    [Test]
    public void AnAdultTitleWithNoGenres_IsExcluded()
        => Assert.That(MatureContentFilter.IsExcluded(isAdult: true, genres: null), Is.True);

    [Test]
    public void ATitleTheLookupSaysNothingAbout_IsKept()
        // Unknown is decided by the recorder (it excludes ids the lookup does not return), not here.
        => Assert.That(MatureContentFilter.IsExcluded(isAdult: null, genres: null), Is.False);

    // ── Finding them ─────────────────────────────────────────────────

    [Test]
    public void MediaIdsIn_FindsMediaWhereverTheyAreNested()
    {
        var response = JsonNode.Parse("""
            {"data":{"Media":{"id":1,"title":{"romaji":"A"},
              "relations":{"edges":[{"relationType":"SEQUEL","node":{"id":2,"title":{"romaji":"B"}}}]},
              "recommendations":{"nodes":[{"rating":3,"mediaRecommendation":{"id":3,"title":{"romaji":"C"}}}]},
              "airing":[{"mediaId":4,"episode":1}]
            }}}
            """);

        Assert.That(MatureContentFilter.MediaIdsIn(response), Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void MediaIdsIn_IgnoresCharactersAndStaff()
    {
        // Characters, staff and studios have ids from separate number ranges that overlap media ids.
        // Looking them up as media would exclude a character because some unrelated title shares
        // its number.
        var response = JsonNode.Parse("""
            {"data":{"Media":{"id":1,"title":{"romaji":"A"},
              "characters":{"edges":[{"node":{"id":10073,"name":{"full":"Someone"}}}]}
            }}}
            """);

        Assert.That(MatureContentFilter.MediaIdsIn(response), Is.EquivalentTo(new[] { 1 }));
    }

    // ── Taking them out ──────────────────────────────────────────────

    [Test]
    public void Remove_DropsTheListEntryNotJustItsMedia()
    {
        // Nulling the media would leave an entry the app renders as a blank row. The whole entry has
        // to go.
        var response = JsonNode.Parse("""
            {"data":{"MediaListCollection":{"lists":[{"name":"Watching","entries":[
              {"id":100,"mediaId":21,"media":{"id":21,"title":{"romaji":"ONE PIECE"}}},
              {"id":101,"mediaId":10073,"media":{"id":10073,"title":{"romaji":"Seikon no Qwaser II"}}}
            ]}]}}}
            """);

        var removed = MatureContentFilter.Remove(response, new HashSet<int> { 10073 });

        var entries = response!["data"]!["MediaListCollection"]!["lists"]![0]!["entries"]!.AsArray();
        Assert.That(removed, Is.EqualTo(1));
        Assert.That(entries.Select(e => e!["mediaId"]!.GetValue<int>()), Is.EqualTo(new[] { 21 }));
    }

    [Test]
    public void Remove_DropsRelationsRecommendationsAndPageResults()
    {
        var response = JsonNode.Parse("""
            {"data":{"Media":{"id":1,"title":{"romaji":"A"},
              "relations":{"edges":[
                {"relationType":"SEQUEL","node":{"id":2,"title":{"romaji":"keep"}}},
                {"relationType":"PREQUEL","node":{"id":6500,"title":{"romaji":"drop"}}}]},
              "recommendations":{"nodes":[
                {"rating":3,"mediaRecommendation":{"id":20678,"title":{"romaji":"drop"}}},
                {"rating":1,"mediaRecommendation":{"id":3,"title":{"romaji":"keep"}}}]}
            },
            "Page":{"media":[{"id":6500,"title":{"romaji":"drop"}},{"id":4,"title":{"romaji":"keep"}}]}}}
            """);

        var removed = MatureContentFilter.Remove(response, new HashSet<int> { 6500, 20678 });

        Assert.That(removed, Is.EqualTo(3));
        Assert.That(MatureContentFilter.MediaIdsIn(response), Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void Remove_DropsAiringEntriesByMediaId()
    {
        // The airing schedule refers to its media by mediaId, with no title alongside.
        var response = JsonNode.Parse("""
            {"data":{"Page":{"airingSchedules":[{"mediaId":10073,"episode":5},{"mediaId":21,"episode":1100}]}}}
            """);

        MatureContentFilter.Remove(response, new HashSet<int> { 10073 });

        var schedules = response!["data"]!["Page"]!["airingSchedules"]!.AsArray();
        Assert.That(schedules.Select(s => s!["mediaId"]!.GetValue<int>()), Is.EqualTo(new[] { 21 }));
    }

    [Test]
    public void Remove_LeavesACharacterAloneWhenItsIdMatchesAnExcludedTitle()
    {
        var response = JsonNode.Parse("""
            {"data":{"Media":{"id":1,"title":{"romaji":"A"},
              "characters":{"edges":[{"role":"MAIN","node":{"id":10073,"name":{"full":"Someone"}}}]}
            }}}
            """);

        var removed = MatureContentFilter.Remove(response, new HashSet<int> { 10073 });

        Assert.That(removed, Is.Zero);
        Assert.That(response!["data"]!["Media"]!["characters"]!["edges"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Remove_DropsAdultTagsFromAnOrdinaryTitle()
    {
        // AniList flags some tags adult on titles that are not. Their descriptions are as explicit as
        // the names suggest, and the title itself is fine to keep.
        var response = JsonNode.Parse("""
            {"data":{"Media":{"id":105778,"title":{"romaji":"Chainsaw Man"},"isAdult":false,"tags":[
              {"id":1,"name":"Gore","isAdult":false},
              {"id":2,"name":"Adult Tag","isAdult":true}
            ]}}}
            """);

        var removed = MatureContentFilter.Remove(response, new HashSet<int>());

        var tags = response!["data"]!["Media"]!["tags"]!.AsArray();
        Assert.That(removed, Is.EqualTo(1));
        Assert.That(tags.Select(t => t!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Gore" }));
    }

    // ── Fixtures that are about one ──────────────────────────────────

    [Test]
    public void IsAbout_TheDetailsOfAnExcludedTitle()
    {
        // Its own details page cannot be filtered into something acceptable. The fixture has to go.
        var response = JsonNode.Parse("""{"data":{"Media":{"id":10073,"title":{"romaji":"Seikon no Qwaser II"}}}}""");

        Assert.That(MatureContentFilter.IsAbout(response, JsonNode.Parse("""{"id":10073}"""), new HashSet<int> { 10073 }), Is.True);
    }

    [Test]
    public void IsAbout_TheTitleItselfEvenWithoutTheRequestVariables()
    {
        // The response's own id comes first; the variables are only for responses that omit it.
        var response = JsonNode.Parse("""{"data":{"Media":{"id":10073,"title":{"romaji":"Seikon no Qwaser II"}}}}""");

        Assert.That(MatureContentFilter.IsAbout(response, variables: null, new HashSet<int> { 10073 }), Is.True);
    }

    [Test]
    public void IsAbout_ASectionPageOfAnExcludedTitle()
    {
        // Section pages select only the connection, so the media id is in the variables.
        var response = JsonNode.Parse("""{"data":{"Media":{"characters":{"edges":[]}}}}""");

        Assert.That(MatureContentFilter.IsAbout(response, JsonNode.Parse("""{"id":10073,"page":1}"""), new HashSet<int> { 10073 }), Is.True);
    }

    [Test]
    public void IsAbout_NotTheDetailsOfAnOrdinaryTitle()
    {
        var response = JsonNode.Parse("""{"data":{"Media":{"id":21,"title":{"romaji":"ONE PIECE"}}}}""");

        Assert.That(MatureContentFilter.IsAbout(response, JsonNode.Parse("""{"id":21}"""), new HashSet<int> { 10073 }), Is.False);
    }

    [Test]
    public void IsAbout_NothingForAResponseWithNoData()
        => Assert.That(MatureContentFilter.IsAbout(JsonNode.Parse("{}"), null, new HashSet<int> { 10073 }), Is.False);

    [Test]
    public void IsAbout_NotACharacterThatSharesTheNumber()
    {
        var response = JsonNode.Parse("""{"data":{"Character":{"id":10073,"name":{"full":"Someone"}}}}""");

        Assert.That(MatureContentFilter.IsAbout(response, JsonNode.Parse("""{"id":10073}"""), new HashSet<int> { 10073 }), Is.False);
    }
}
