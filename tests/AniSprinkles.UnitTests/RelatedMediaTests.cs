using AniSprinkles.Models;

namespace AniSprinkles.UnitTests;

public class RelatedMediaTests
{
    // IsAnime gates detail-page navigation: the Media query is anime-only, so tapping a
    // non-anime tile (manga/novel) must short-circuit to a toast instead of a 404'ing fetch.
    [Theory]
    [InlineData("ANIME", true)]
    [InlineData("anime", true)]   // AniList sends upper-case, but guard is case-insensitive by design.
    [InlineData("MANGA", false)]
    [InlineData("NOVEL", false)]
    [InlineData("ONE_SHOT", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAnime_ReflectsType(string? type, bool expected)
    {
        var media = new RelatedMedia { Id = 1, Type = type };

        Assert.Equal(expected, media.IsAnime);
    }
}
