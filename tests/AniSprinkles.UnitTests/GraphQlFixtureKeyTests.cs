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

    [Test]
    public void PropertyOrder_DoesNotChangeTheKey()
    {
        // Variables are built from anonymous objects, so member order is a compile-time accident
        // rather than anything meaningful. If it leaked into the key, reordering a variables object
        // during an unrelated refactor would orphan every fixture that operation ever recorded.
        var first = GraphQlFixtureKey.Derive("Media", Vars("""{"id":21,"page":1}"""));
        var second = GraphQlFixtureKey.Derive("Media", Vars("""{"page":1,"id":21}"""));

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void DifferentVariables_ProduceDifferentKeys()
    {
        var first = GraphQlFixtureKey.Derive("Media", Vars("""{"id":21}"""));
        var second = GraphQlFixtureKey.Derive("Media", Vars("""{"id":22}"""));

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void DifferentOperations_ProduceDifferentKeys()
    {
        var characters = GraphQlFixtureKey.Derive("MediaCharactersPage", Vars("""{"id":21}"""));
        var staff = GraphQlFixtureKey.Derive("MediaStaffPage", Vars("""{"id":21}"""));

        Assert.That(staff, Is.Not.EqualTo(characters));
    }

    [Test]
    public void ArrayOrder_DoesChangeTheKey()
    {
        // Unlike object members, GraphQL list arguments are ordered and meaningful — a sort of
        // [ROLE, RELEVANCE] is not the same request as [RELEVANCE, ROLE].
        var first = GraphQlFixtureKey.Derive("MediaCharactersPage", Vars("""{"sort":["ROLE","ID"]}"""));
        var second = GraphQlFixtureKey.Derive("MediaCharactersPage", Vars("""{"sort":["ID","ROLE"]}"""));

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void NullVariables_AreStableAndDistinctFromEmpty()
    {
        var none = GraphQlFixtureKey.Derive("ViewerFull", null);
        var alsoNone = GraphQlFixtureKey.Derive("ViewerFull", null);

        Assert.That(alsoNone, Is.EqualTo(none));
        Assert.That(GraphQlFixtureKey.Derive("ViewerFull", Vars("{}")), Is.Not.EqualTo(none));
    }

    // ── Clock-derived variables ──────────────────────────────────────

    [Test]
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

        Assert.That(winter, Is.EqualTo(autumn));
    }

    [Test]
    public void DiscoverSections_StillDistinguishesTheAdultFilter()
    {
        // Dropping the volatile names must not flatten the axis the canary contract depends on.
        var sfw = GraphQlFixtureKey.Derive("DiscoverSections", Vars("""{"season":"FALL","isAdult":false}"""));
        var unfiltered = GraphQlFixtureKey.Derive("DiscoverSections", Vars("""{"season":"FALL"}"""));

        Assert.That(unfiltered, Is.Not.EqualTo(sfw));
    }

    [Test]
    public void AiringSchedule_IgnoresTheWindowAndMediaIds()
    {
        var monday = GraphQlFixtureKey.Derive(
            "AiringSchedule",
            Vars("""{"mediaIds":[21,16498],"airingAfter":1000,"airingBefore":2000,"page":1}"""));
        var thursday = GraphQlFixtureKey.Derive(
            "AiringSchedule",
            Vars("""{"mediaIds":[1,2,3],"airingAfter":9000,"airingBefore":9999,"page":1}"""));

        Assert.That(thursday, Is.EqualTo(monday));
    }

    [Test]
    public void AiringSchedule_StillDistinguishesThePage()
    {
        var first = GraphQlFixtureKey.Derive("AiringSchedule", Vars("""{"airingAfter":1,"page":1}"""));
        var second = GraphQlFixtureKey.Derive("AiringSchedule", Vars("""{"airingAfter":1,"page":2}"""));

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void BrowseAnime_IgnoresTheSeasonButStatusStillSeparatesAiringFromUpcoming()
    {
        // The two View All pages that pin a season differ by status as well — RELEASING for Airing,
        // NOT_YET_RELEASED for Upcoming — which is what makes dropping the season safe. If that ever
        // stopped being true they would collide onto one fixture, so it is pinned here.
        var airingNow = Vars("""{"sort":"POPULARITY_DESC","status":"RELEASING","season":"FALL","seasonYear":2026,"page":1}""");
        var airingLater = Vars("""{"sort":"POPULARITY_DESC","status":"RELEASING","season":"WINTER","seasonYear":2027,"page":1}""");
        var upcoming = Vars("""{"sort":"POPULARITY_DESC","status":"NOT_YET_RELEASED","season":"WINTER","seasonYear":2027,"page":1}""");

        Assert.That(
            GraphQlFixtureKey.Derive("BrowseAnime", airingLater),
            Is.EqualTo(GraphQlFixtureKey.Derive("BrowseAnime", airingNow)));

        Assert.That(
            GraphQlFixtureKey.Derive("BrowseAnime", upcoming),
            Is.Not.EqualTo(GraphQlFixtureKey.Derive("BrowseAnime", airingLater)));
    }

    [Test]
    public void VolatileNames_AreStrippedAtTheTopLevelOnly()
    {
        // "season" nested inside another object is a field of some other thing, not the clock-derived
        // argument. Stripping it everywhere would silently merge two genuinely different requests.
        var first = GraphQlFixtureKey.Derive("DiscoverSections", Vars("""{"filter":{"season":"FALL"}}"""));
        var second = GraphQlFixtureKey.Derive("DiscoverSections", Vars("""{"filter":{"season":"WINTER"}}"""));

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void AnOperationWithNoVolatileNames_KeysOnEverything()
    {
        var first = GraphQlFixtureKey.Derive("Media", Vars("""{"id":21,"season":"FALL"}"""));
        var second = GraphQlFixtureKey.Derive("Media", Vars("""{"id":21,"season":"WINTER"}"""));

        Assert.That(second, Is.Not.EqualTo(first));
    }

    // ── Query fingerprint ────────────────────────────────────────────

    [Test]
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

        Assert.That(sprawling, Is.EqualTo(compact));
    }

    [Test]
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

        Assert.That(adult, Is.Not.EqualTo(plain));

        // They still share an address, which is what lets a lookup find them both and choose.
        Assert.That(
            GraphQlFixtureKey.Derive("DiscoverSections", variables),
            Is.EqualTo(GraphQlFixtureKey.Derive("DiscoverSections", variables)));
        Assert.That(plain, Does.StartWith(GraphQlFixtureKey.Derive("DiscoverSections", variables)));
        Assert.That(adult, Does.StartWith(GraphQlFixtureKey.Derive("DiscoverSections", variables)));
    }

    [Test]
    public void QueryFingerprint_ChangesWhenAFieldIsAdded()
    {
        var before = GraphQlFixtureKey.QueryFingerprint("query Media { Media { id } }");
        var after = GraphQlFixtureKey.QueryFingerprint("query Media { Media { id title } }");

        Assert.That(after, Is.Not.EqualTo(before));
    }

    private static JsonNode? Vars(string json) => JsonNode.Parse(json);
}
