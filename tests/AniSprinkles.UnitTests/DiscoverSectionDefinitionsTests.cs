namespace AniSprinkles.UnitTests;

public class DiscoverSectionDefinitionsTests
{
    [Test]
    public void Every_section_has_exactly_one_definition()
    {
        foreach (var section in Enum.GetValues<DiscoverSection>())
        {
            Assert.That(DiscoverSectionDefinitions.All, Has.Exactly(1).Matches<DiscoverSectionDefinition>(definition => definition is not null && definition.Section == section));
            Assert.That(DiscoverSectionDefinitions.Get(section).Section, Is.EqualTo(section));
        }
    }

    [Test]
    public void Season_scoped_sections_carry_a_status_filter()
    {
        // A season filter without a status filter (or vice versa) would silently change a row's meaning.
        foreach (var definition in DiscoverSectionDefinitions.All.Where(d => d.SeasonKind != DiscoverSeasonKind.None))
        {
            Assert.That(string.IsNullOrEmpty(definition.Status), Is.False);
        }
    }

    [Test]
    public void Exactly_the_score_sorted_top_lists_show_rank()
    {
        // Rank numbers only make sense on "Top …" lists; popularity/trending rows are unranked.
        var ranked = DiscoverSectionDefinitions.All.Where(d => d.ShowsRank).Select(d => d.Section).ToList();

        Assert.That(
            ranked,
            Is.EqualTo([DiscoverSection.Top, DiscoverSection.TopMovies, DiscoverSection.TopRatedAdult]));
        Assert.Multiple(() =>
        {
            foreach (var definition in DiscoverSectionDefinitions.All.Where(d => d.ShowsRank))
            {
                Assert.That(definition.Sort, Is.EqualTo("SCORE_DESC"), definition.Section.ToString());
            }
        });
    }

    [Test]
    public void The_18_plus_pair_are_the_only_explicitly_adult_sections()
    {
        var adult = DiscoverSectionDefinitions.All.Where(d => d.AdultFilter == true).Select(d => d.Section).ToList();

        Assert.That(adult, Is.EqualTo([DiscoverSection.PopularAdult, DiscoverSection.TopRatedAdult]));
    }

    [Test]
    public void Top_movies_is_the_only_format_pinned_section()
    {
        var matches = DiscoverSectionDefinitions.All.Where(definition => definition.Format is not null).ToList();
        Assert.That(matches, Has.Exactly(1).Items);
        var pinned = matches[0];

        Assert.That(pinned.Section, Is.EqualTo(DiscoverSection.TopMovies));
        Assert.That(pinned.Format, Is.EqualTo("MOVIE"));
    }
}
