namespace AniSprinkles.UnitTests;

public class DiscoverSectionDefinitionsTests
{
    [Fact]
    public void Every_section_has_exactly_one_definition()
    {
        foreach (var section in Enum.GetValues<DiscoverSection>())
        {
            Assert.Single(DiscoverSectionDefinitions.All, definition => definition.Section == section);
            Assert.Equal(section, DiscoverSectionDefinitions.Get(section).Section);
        }
    }

    [Fact]
    public void Season_scoped_sections_carry_a_status_filter()
    {
        // A season filter without a status filter (or vice versa) would silently change a row's meaning.
        foreach (var definition in DiscoverSectionDefinitions.All.Where(d => d.SeasonKind != DiscoverSeasonKind.None))
        {
            Assert.False(string.IsNullOrEmpty(definition.Status));
        }
    }

    [Fact]
    public void Exactly_the_score_sorted_top_lists_show_rank()
    {
        // Rank numbers only make sense on "Top …" lists; popularity/trending rows are unranked.
        var ranked = DiscoverSectionDefinitions.All.Where(d => d.ShowsRank).Select(d => d.Section).ToList();

        Assert.Equal(
            [DiscoverSection.Top, DiscoverSection.TopMovies, DiscoverSection.TopRatedAdult],
            ranked);
        Assert.All(
            DiscoverSectionDefinitions.All.Where(d => d.ShowsRank),
            d => Assert.Equal("SCORE_DESC", d.Sort));
    }

    [Fact]
    public void The_18_plus_pair_are_the_only_explicitly_adult_sections()
    {
        var adult = DiscoverSectionDefinitions.All.Where(d => d.AdultFilter == true).Select(d => d.Section).ToList();

        Assert.Equal([DiscoverSection.PopularAdult, DiscoverSection.TopRatedAdult], adult);
    }

    [Fact]
    public void Top_movies_is_the_only_format_pinned_section()
    {
        var pinned = Assert.Single(DiscoverSectionDefinitions.All, definition => definition.Format is not null);

        Assert.Equal(DiscoverSection.TopMovies, pinned.Section);
        Assert.Equal("MOVIE", pinned.Format);
    }
}
