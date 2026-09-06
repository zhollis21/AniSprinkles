using System.Text.Json.Nodes;
using AniSprinkles.Services.Fixtures;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #134's addressing scheme: how a recorded AniList response is found again.
/// <para>
/// The recorder and the CI replay handler both derive keys through this type and neither keeps its
/// own copy, so these tests are the contract between them. A disagreement would land a recording
/// under one name and look it up under another, which surfaces as "fixture missing" at exactly the
/// moment the fixtures are supposed to be proving something.
/// </para>
/// </summary>
public class GraphQlFixtureKeyTests
{
    // ── Identity ─────────────────────────────────────────────────────

    [Fact]
    public void PropertyOrder_DoesNotChangeTheKey()
    {
        // Variables are built from anonymous objects, so member order is a compile-time accident
        // rather than anything meaningful. If it leaked into the key, reordering a variables object
        // during an unrelated refactor would orphan every fixture that operation ever recorded.
        var first = GraphQlFixtureKey.Derive("Media", Vars("""{"id":21,"page":1}"""));
        var second = GraphQlFixtureKey.Derive("Media", Vars("""{"page":1,"id":21}"""));

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentVariables_ProduceDifferentKeys()
    {
        var first = GraphQlFixtureKey.Derive("Media", Vars("""{"id":21}"""));
        var second = GraphQlFixtureKey.Derive("Media", Vars("""{"id":22}"""));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DifferentOperations_ProduceDifferentKeys()
    {
        var characters = GraphQlFixtureKey.Derive("MediaCharactersPage", Vars("""{"id":21}"""));
        var staff = GraphQlFixtureKey.Derive("MediaStaffPage", Vars("""{"id":21}"""));

        Assert.NotEqual(characters, staff);
    }

    [Fact]
    public void ArrayOrder_DoesChangeTheKey()
    {
        // Unlike object members, GraphQL list arguments are ordered and meaningful — a sort of
        // [ROLE, RELEVANCE] is not the same request as [RELEVANCE, ROLE].
        var first = GraphQlFixtureKey.Derive("MediaCharactersPage", Vars("""{"sort":["ROLE","ID"]}"""));
        var second = GraphQlFixtureKey.Derive("MediaCharactersPage", Vars("""{"sort":["ID","ROLE"]}"""));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NullVariables_AreStableAndDistinctFromEmpty()
    {
        var none = GraphQlFixtureKey.Derive("ViewerFull", null);
        var alsoNone = GraphQlFixtureKey.Derive("ViewerFull", null);

        Assert.Equal(none, alsoNone);
        Assert.NotEqual(none, GraphQlFixtureKey.Derive("ViewerFull", Vars("{}")));
    }

    // ── Clock-derived variables ──────────────────────────────────────

    [Fact]
    public void DiscoverSections_IgnoresTheSeasonAndYear()
    {
        // The season comes from AniListSeason.Current(now). Keyed on, a fixture recorded in autumn
        // would stop resolving in winter — a CI failure with nothing to do with the code under test.
        var autumn = GraphQlFixtureKey.Derive(
            "DiscoverSections",
            Vars("""{"season":"FALL","seasonYear":2026,"nextSeason":"WINTER","nextSeasonYear":2027,"perPage":20}"""));
        var winter = GraphQlFixtureKey.Derive(
            "DiscoverSections",
            Vars("""{"season":"WINTER","seasonYear":2027,"nextSeason":"SPRING","nextSeasonYear":2027,"perPage":20}"""));

        Assert.Equal(autumn, winter);
    }

    [Fact]
    public void DiscoverSections_StillDistinguishesTheAdultFilter()
    {
        // Dropping the volatile names must not flatten the axis the canary contract depends on.
        var sfw = GraphQlFixtureKey.Derive("DiscoverSections", Vars("""{"season":"FALL","isAdult":false}"""));
        var unfiltered = GraphQlFixtureKey.Derive("DiscoverSections", Vars("""{"season":"FALL"}"""));

        Assert.NotEqual(sfw, unfiltered);
    }

    [Fact]
    public void AiringSchedule_IgnoresTheWindowAndMediaIds()
    {
        var monday = GraphQlFixtureKey.Derive(
            "AiringSchedule",
            Vars("""{"mediaIds":[21,16498],"airingAfter":1000,"airingBefore":2000,"page":1}"""));
        var thursday = GraphQlFixtureKey.Derive(
            "AiringSchedule",
            Vars("""{"mediaIds":[1,2,3],"airingAfter":9000,"airingBefore":9999,"page":1}"""));

        Assert.Equal(monday, thursday);
    }

    [Fact]
    public void AiringSchedule_StillDistinguishesThePage()
    {
        var first = GraphQlFixtureKey.Derive("AiringSchedule", Vars("""{"airingAfter":1,"page":1}"""));
        var second = GraphQlFixtureKey.Derive("AiringSchedule", Vars("""{"airingAfter":1,"page":2}"""));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BrowseAnime_IgnoresTheSeasonButStatusStillSeparatesAiringFromUpcoming()
    {
        // The two View All pages that pin a season differ by status as well — RELEASING for Airing,
        // NOT_YET_RELEASED for Upcoming — which is what makes dropping the season safe. If that ever
        // stopped being true they would collide onto one fixture, so it is pinned here.
        var airingNow = Vars("""{"sort":"POPULARITY_DESC","status":"RELEASING","season":"FALL","seasonYear":2026,"page":1}""");
        var airingLater = Vars("""{"sort":"POPULARITY_DESC","status":"RELEASING","season":"WINTER","seasonYear":2027,"page":1}""");
        var upcoming = Vars("""{"sort":"POPULARITY_DESC","status":"NOT_YET_RELEASED","season":"WINTER","seasonYear":2027,"page":1}""");

        Assert.Equal(
            GraphQlFixtureKey.Derive("BrowseAnime", airingNow),
            GraphQlFixtureKey.Derive("BrowseAnime", airingLater));

        Assert.NotEqual(
            GraphQlFixtureKey.Derive("BrowseAnime", airingLater),
            GraphQlFixtureKey.Derive("BrowseAnime", upcoming));
    }

    [Fact]
    public void VolatileNames_AreStrippedAtTheTopLevelOnly()
    {
        // "season" nested inside another object is a field of some other thing, not the clock-derived
        // argument. Stripping it everywhere would silently merge two genuinely different requests.
        var first = GraphQlFixtureKey.Derive("DiscoverSections", Vars("""{"filter":{"season":"FALL"}}"""));
        var second = GraphQlFixtureKey.Derive("DiscoverSections", Vars("""{"filter":{"season":"WINTER"}}"""));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AnOperationWithNoVolatileNames_KeysOnEverything()
    {
        var first = GraphQlFixtureKey.Derive("Media", Vars("""{"id":21,"season":"FALL"}"""));
        var second = GraphQlFixtureKey.Derive("Media", Vars("""{"id":21,"season":"WINTER"}"""));

        Assert.NotEqual(first, second);
    }

    // ── Query fingerprint ────────────────────────────────────────────

    [Fact]
    public void QueryFingerprint_IgnoresReformatting()
    {
        // Replay refuses a fixture whose query fingerprint has moved. If that tripped on whitespace,
        // reindenting a query would invalidate every recording it ever produced.
        var compact = GraphQlFixtureKey.QueryFingerprint("query Media($id: Int!) { Media(id: $id) { id } }");
        var sprawling = GraphQlFixtureKey.QueryFingerprint(
            """
            query Media($id: Int!) {
              Media(id: $id) {
                id
              }
            }
            """);

        Assert.Equal(compact, sprawling);
    }

    [Fact]
    public void TwoQueriesUnderIdenticalVariables_GetDifferentFiles()
    {
        // GetDiscoverSectionsAsync is the case that forced this: it sends one document with the 18+
        // aliases and one without, under identical variables, because which document to send is a
        // caller decision rather than a GraphQL argument. Addressed on variables alone the two
        // collapse onto one file and the second silently overwrites the first — after which replay
        // answers whichever query the app sends with the other one's response.
        var variables = Vars("""{"perPage":20,"isAdult":false}""");
        var withoutAdult = GraphQlFixtureKey.QueryFingerprint("query DiscoverSections { airing { id } }");
        var withAdult = GraphQlFixtureKey.QueryFingerprint("query DiscoverSections { airing { id } adult { id } }");

        var plain = GraphQlFixtureKey.FileName("DiscoverSections", variables, withoutAdult);
        var adult = GraphQlFixtureKey.FileName("DiscoverSections", variables, withAdult);

        Assert.NotEqual(plain, adult);

        // They still share an address, which is what lets a lookup find them both and choose.
        Assert.Equal(
            GraphQlFixtureKey.Derive("DiscoverSections", variables),
            GraphQlFixtureKey.Derive("DiscoverSections", variables));
        Assert.StartsWith(GraphQlFixtureKey.Derive("DiscoverSections", variables), plain, StringComparison.Ordinal);
        Assert.StartsWith(GraphQlFixtureKey.Derive("DiscoverSections", variables), adult, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryFingerprint_ChangesWhenAFieldIsAdded()
    {
        var before = GraphQlFixtureKey.QueryFingerprint("query Media { Media { id } }");
        var after = GraphQlFixtureKey.QueryFingerprint("query Media { Media { id title } }");

        Assert.NotEqual(before, after);
    }

    private static JsonNode? Vars(string json) => JsonNode.Parse(json);
}
