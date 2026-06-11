using AniSprinkles.Models;
using AniSprinkles.PageModels;

namespace AniSprinkles.UnitTests;

public class DetailsListSortersTests
{
    private static CharacterMediaEdge Media(int id, int? pop = null, int? score = null, int? fav = null, int? year = null, int? month = null, int? day = null, string? title = null)
        => new()
        {
            Node = new RelatedMedia
            {
                Id = id,
                Popularity = pop,
                AverageScore = score,
                Favourites = fav,
                StartDate = year is null && month is null && day is null ? null : new MediaDate { Year = year, Month = month, Day = day },
                Title = title is null ? null : new MediaTitle { Romaji = title },
            },
        };

    private static StaffMediaEdge ProdMedia(int id, int? pop = null)
        => new() { Node = new RelatedMedia { Id = id, Popularity = pop } };

    private static StudioMediaEdge StudioMedia(int id, int? pop = null, int? year = null)
        => new()
        {
            Node = new RelatedMedia
            {
                Id = id,
                Popularity = pop,
                StartDate = year is null ? null : new MediaDate { Year = year },
            },
        };

    private static StaffCharacterEdge VoiceRole(int id, string? role = null, int? fav = null)
        => new() { Role = role, Node = new Character { Id = id, Favourites = fav } };

    // A fixed set where every dimension yields a distinct order, so each sort code is unambiguous.
    private static IReadOnlyList<CharacterMediaEdge> Sample() =>
    [
        Media(1, pop: 10, score: 90, fav: 100, year: 2010, title: "Beta"),
        Media(2, pop: 30, score: 70, fav: 300, year: 2000, title: "alpha"),
        Media(3, pop: 20, score: 80, fav: 200, year: 2020, title: "Gamma"),
    ];

    [Theory]
    [InlineData("POPULARITY_DESC", new[] { 2, 3, 1 })]
    [InlineData("SCORE_DESC", new[] { 1, 3, 2 })]
    [InlineData("FAVOURITES_DESC", new[] { 2, 3, 1 })]
    [InlineData("START_DATE_DESC", new[] { 3, 1, 2 })] // newest first
    [InlineData("START_DATE", new[] { 2, 1, 3 })]      // oldest first
    [InlineData("TITLE_ROMAJI", new[] { 2, 1, 3 })]    // alpha, Beta, Gamma (case-insensitive)
    [InlineData("UNRECOGNIZED", new[] { 2, 3, 1 })]    // falls back to popularity
    public void SortAppearances_ByCode_OrdersAsExpected(string sort, int[] expectedIds)
    {
        var result = DetailsListSorters.SortAppearances(sort, Sample());

        Assert.Equal(expectedIds, result.Select(e => e.Node!.Id));
    }

    [Fact]
    public void SortAppearances_EqualKeys_TieBreaksByIdAscending_Deterministically()
    {
        // All equal popularity, supplied in descending-id order; result must be id-ascending and
        // identical regardless of input order (so A→B→A toggles don't shuffle).
        var items = new List<CharacterMediaEdge> { Media(3, pop: 5), Media(1, pop: 5), Media(2, pop: 5) };

        var first = DetailsListSorters.SortAppearances("POPULARITY_DESC", items);
        var second = DetailsListSorters.SortAppearances("POPULARITY_DESC", first);

        Assert.Equal(new[] { 1, 2, 3 }, first.Select(e => e.Node!.Id));
        Assert.Equal(new[] { 1, 2, 3 }, second.Select(e => e.Node!.Id));
    }

    [Fact]
    public void SortAppearances_StartDate_FloatsUndatedFirstBothDirections()
    {
        // Mirrors AniList's server order: undated entries first in BOTH directions, then by date.
        var items = new List<CharacterMediaEdge> { Media(1, year: 2005), Media(2, year: null), Media(3, year: 1990) };

        var oldest = DetailsListSorters.SortAppearances("START_DATE", items);
        var newest = DetailsListSorters.SortAppearances("START_DATE_DESC", items);

        Assert.Equal(new[] { 2, 3, 1 }, oldest.Select(e => e.Node!.Id)); // (undated), 1990, 2005
        Assert.Equal(new[] { 2, 1, 3 }, newest.Select(e => e.Node!.Id)); // (undated), 2005, 1990
    }

    [Fact]
    public void SortAppearances_StartDate_SameYearOrdersByMonthThenDay()
    {
        var items = new List<CharacterMediaEdge>
        {
            Media(1, year: 2020, month: 6, day: 1),
            Media(2, year: 2020, month: 1, day: 15),
            Media(3, year: 2020, month: 1, day: 2),
        };

        var oldest = DetailsListSorters.SortAppearances("START_DATE", items);
        var newest = DetailsListSorters.SortAppearances("START_DATE_DESC", items);

        Assert.Equal(new[] { 3, 2, 1 }, oldest.Select(e => e.Node!.Id)); // Jan 2, Jan 15, Jun 1
        Assert.Equal(new[] { 1, 2, 3 }, newest.Select(e => e.Node!.Id)); // Jun 1, Jan 15, Jan 2
    }

    [Fact]
    public void SortAppearances_ByTitle_UntitledSortsLast()
    {
        var items = new List<CharacterMediaEdge> { Media(1, title: "Beta"), Media(2, title: null), Media(3, title: "alpha") };

        var result = DetailsListSorters.SortAppearances("TITLE_ROMAJI", items);

        Assert.Equal(new[] { 3, 1, 2 }, result.Select(e => e.Node!.Id)); // alpha, Beta, (untitled)
    }

    [Fact]
    public void SortAppearances_NullNode_DoesNotThrowAndSortsLast()
    {
        var items = new List<CharacterMediaEdge> { new() { Node = null }, Media(5, pop: 50) };

        var result = DetailsListSorters.SortAppearances("POPULARITY_DESC", items);

        Assert.Equal(5, result[0].Node!.Id);
        Assert.Null(result[1].Node);
    }

    [Theory]
    [InlineData("POPULARITY_DESC")]
    [InlineData("TITLE_ROMAJI")] // null title → empty string ties with the null node's empty string
    public void SortAppearances_NullNode_SortsAfterRealItemEvenWhenKeyTies(string sort)
    {
        // Real item has a zero/empty key, so it ties the null node on the primary key; the null node
        // must still come last (it would otherwise win the id=0 tiebreak).
        var items = new List<CharacterMediaEdge> { new() { Node = null }, Media(7, pop: 0, title: null) };

        var result = DetailsListSorters.SortAppearances(sort, items);

        Assert.Equal(7, result[0].Node!.Id);
        Assert.Null(result[1].Node);
    }

    [Fact]
    public void SortVoiceRoles_NullNode_SortsAfterRealItemEvenWhenFavouritesZero()
    {
        var items = new List<StaffCharacterEdge> { new() { Node = null }, VoiceRole(7, fav: 0) };

        var result = DetailsListSorters.SortVoiceRoles("FAVOURITES_DESC", items);

        Assert.Equal(7, result[0].Node!.Id);
        Assert.Null(result[1].Node);
    }

    [Fact]
    public void SortProductionRoles_SharesMediaSortLogic()
    {
        var items = new List<StaffMediaEdge> { ProdMedia(1, pop: 10), ProdMedia(2, pop: 30), ProdMedia(3, pop: 20) };

        var result = DetailsListSorters.SortProductionRoles("POPULARITY_DESC", items);

        Assert.Equal(new[] { 2, 3, 1 }, result.Select(e => e.Node!.Id));
    }

    [Fact]
    public void SortStudioProductions_SharesMediaSortLogic()
    {
        var items = new List<StudioMediaEdge>
        {
            StudioMedia(1, pop: 10, year: 2010),
            StudioMedia(2, pop: 30, year: null),
            StudioMedia(3, pop: 20, year: 2000),
        };

        var byPopularity = DetailsListSorters.SortStudioProductions("POPULARITY_DESC", items);
        var byOldest = DetailsListSorters.SortStudioProductions("START_DATE", items);

        Assert.Equal(new[] { 2, 3, 1 }, byPopularity.Select(e => e.Node!.Id));
        Assert.Equal(new[] { 2, 3, 1 }, byOldest.Select(e => e.Node!.Id)); // (undated), 2000, 2010
    }

    [Fact]
    public void SortVoiceRoles_ByRole_OrdersMainSupportingBackgroundThenOther()
    {
        var items = new List<StaffCharacterEdge>
        {
            VoiceRole(1, "SUPPORTING"),
            VoiceRole(2, "MAIN"),
            VoiceRole(3, "BACKGROUND"),
            VoiceRole(4, null),
        };

        var result = DetailsListSorters.SortVoiceRoles("ROLE", items);

        Assert.Equal(new[] { 2, 1, 3, 4 }, result.Select(e => e.Node!.Id));
    }

    [Fact]
    public void SortVoiceRoles_SameRole_TieBreaksByFavouritesThenId()
    {
        var items = new List<StaffCharacterEdge>
        {
            VoiceRole(1, "MAIN", fav: 50),
            VoiceRole(2, "MAIN", fav: 500),
            VoiceRole(3, "MAIN", fav: 500),
        };

        var result = DetailsListSorters.SortVoiceRoles("ROLE", items);

        Assert.Equal(new[] { 2, 3, 1 }, result.Select(e => e.Node!.Id)); // 500(id2), 500(id3), 50(id1)
    }

    [Fact]
    public void SortVoiceRoles_Default_OrdersByFavouritesDescending()
    {
        var items = new List<StaffCharacterEdge>
        {
            VoiceRole(1, fav: 100),
            VoiceRole(2, fav: 300),
            VoiceRole(3, fav: 200),
        };

        var result = DetailsListSorters.SortVoiceRoles("FAVOURITES_DESC", items);

        Assert.Equal(new[] { 2, 3, 1 }, result.Select(e => e.Node!.Id));
    }

    // ---- Relations (client-side; RelationType is the mapper's display string, e.g. "Side Story") ----

    private static MediaRelationEdge Relation(int id, string? type = null, int? year = null, string? title = null)
        => new()
        {
            RelationType = type,
            Node = new RelatedMedia
            {
                Id = id,
                StartDate = year is null ? null : new MediaDate { Year = year },
                Title = title is null ? null : new MediaTitle { Romaji = title },
            },
        };

    [Fact]
    public void SortRelations_ByRelationType_OrdersByCuratedNarrativeOrder()
    {
        var items = new List<MediaRelationEdge>
        {
            Relation(1, "Spin Off"),
            Relation(2, "Sequel"),
            Relation(3, "Other"),
            Relation(4, "Prequel"),
            Relation(5, "Side Story"),
            Relation(6, "Parent"),
            Relation(7, "Adaptation"),
            Relation(8, "Alternative"),
        };

        var result = DetailsListSorters.SortRelations("RELATION", items);

        // Sequel → Prequel → Side Story → Parent → Adaptation → Spin Off → Alternative → other
        Assert.Equal(new[] { 2, 4, 5, 6, 7, 1, 8, 3 }, result.Select(e => e.Node!.Id));
    }

    [Fact]
    public void SortRelations_UnknownAndNullTypes_LandInTrailingBucketStableById()
    {
        var items = new List<MediaRelationEdge>
        {
            Relation(30, "Other"),
            Relation(10, null),
            Relation(20, "Character"), // a real AniList type we don't curate → trailing bucket
            Relation(5, "Sequel"),
        };

        var result = DetailsListSorters.SortRelations("RELATION", items);

        Assert.Equal(new[] { 5, 10, 20, 30 }, result.Select(e => e.Node!.Id));
    }

    [Fact]
    public void SortRelations_ByYear_FloatsUndatedFirstBothDirections()
    {
        // Same rule as the productions lists: undated first in both directions, then by date.
        var items = new List<MediaRelationEdge>
        {
            Relation(1, year: 2005),
            Relation(2, year: null),
            Relation(3, year: 1990),
        };

        var newest = DetailsListSorters.SortRelations("YEAR_DESC", items);
        var oldest = DetailsListSorters.SortRelations("YEAR_ASC", items);

        Assert.Equal(new[] { 2, 1, 3 }, newest.Select(e => e.Node!.Id)); // (undated), 2005, 1990
        Assert.Equal(new[] { 2, 3, 1 }, oldest.Select(e => e.Node!.Id)); // (undated), 1990, 2005
    }

    [Fact]
    public void SortRelations_ByTitle_CaseInsensitiveWithNullTitlesLast()
    {
        var items = new List<MediaRelationEdge>
        {
            Relation(1, title: "Beta"),
            Relation(2, title: "alpha"),
            Relation(3, title: null),
        };

        var result = DetailsListSorters.SortRelations("TITLE", items);

        Assert.Equal(new[] { 2, 1, 3 }, result.Select(e => e.Node!.Id)); // alpha, Beta, (untitled)
    }

    [Fact]
    public void SortRelations_NullNode_SortsLast()
    {
        var items = new List<MediaRelationEdge> { new() { Node = null, RelationType = "Sequel" }, Relation(5, "Sequel") };

        var result = DetailsListSorters.SortRelations("RELATION", items);

        Assert.Equal(5, result[0].Node!.Id);
        Assert.Null(result[1].Node);
    }

    [Fact]
    public void SortRelations_ToggleBackToRelation_IsStable()
    {
        var items = new List<MediaRelationEdge>
        {
            Relation(1, "Side Story", year: 2010),
            Relation(2, "Sequel", year: 2000),
            Relation(3, "Prequel", year: 2020),
        };

        var byRelation = DetailsListSorters.SortRelations("RELATION", items);
        var byYear = DetailsListSorters.SortRelations("YEAR_DESC", byRelation);
        var backToRelation = DetailsListSorters.SortRelations("RELATION", byYear);

        Assert.Equal(byRelation.Select(e => e.Node!.Id), backToRelation.Select(e => e.Node!.Id));
    }
}
