namespace AniSprinkles.UnitTests;

/// <summary>
/// #137. Markdown links in character and staff bios overwhelmingly point back at AniList entities the
/// app already has pages for — of 235 links sampled across the 50 most-favourited characters and
/// staff, 162 were anilist.co character/anime/staff/studio URLs. This is the pure half of
/// making them tappable: deciding whether a URL is one of ours and which route it maps to. The
/// Android side that swaps <c>URLSpan</c> for a tappable, coloured span can't be tested off-device,
/// so everything that can live here does.
/// </summary>
public class AniListLinkTargetTests
{
    [Theory]
    [InlineData("https://anilist.co/character/725", "character-details", "characterId", 725)]
    [InlineData("https://anilist.co/staff/96881", "staff-details", "staffId", 96881)]
    [InlineData("https://anilist.co/anime/21", "media-details", "mediaId", 21)]
    [InlineData("https://anilist.co/studio/61", "studio-details", "studioId", 61)]
    public void AnAniListEntityUrl_ResolvesToItsRoute(string url, string route, string parameterName, int id)
    {
        var target = AniListLinkTarget.Resolve(url);

        Assert.NotNull(target);
        Assert.Equal(route, target.Route);
        Assert.Equal(parameterName, target.ParameterName);
        Assert.Equal(id, target.Id);
    }

    [Fact]
    public void ATrailingSlugSegment_IsIgnored()
    {
        // AniList's own links carry the name after the id, e.g. /character/725/Buggy-the-Clown.
        var target = AniListLinkTarget.Resolve("https://anilist.co/character/725/Buggy-the-Clown");

        Assert.NotNull(target);
        Assert.Equal("character-details", target.Route);
        Assert.Equal(725, target.Id);
    }

    [Theory]
    [InlineData("http://anilist.co/character/725")]
    [InlineData("https://www.anilist.co/character/725")]
    [InlineData("https://AniList.co/Character/725")]
    [InlineData("https://anilist.co/character/725/")]
    public void HostSchemeAndCasingVariants_AllResolve(string url)
    {
        var target = AniListLinkTarget.Resolve(url);

        Assert.NotNull(target);
        Assert.Equal("character-details", target.Route);
        Assert.Equal(725, target.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://en.wikipedia.org/wiki/Monkey_D._Luffy")]
    [InlineData("https://twitter.com/tsuda_ken")]
    [InlineData("https://anilist.co")]
    [InlineData("https://anilist.co/character")]
    [InlineData("https://anilist.co/character/not-a-number")]
    [InlineData("https://anilist.co/user/someone")]
    [InlineData("https://anilist.co/manga")]
    [InlineData("https://anilist.co/manga/not-a-number")]
    [InlineData("https://anilist.co/character/-5")]
    [InlineData("https://notanilist.co/character/725")]
    [InlineData("https://notanilist.co/manga/30013")]
    [InlineData("https://anilist.co.evil.example/character/725")]
    public void AnythingElse_DoesNotResolve(string? url)
        // Everything that returns null goes to the external browser instead — 66 of the 235 sampled
        // links are staff social and agency pages, which is a perfectly good outcome for them.
        => Assert.Null(AniListLinkTarget.Resolve(url));

    [Theory]
    [InlineData("https://anilist.co/manga/30013")]
    [InlineData("https://anilist.co/manga/30013/Berserk")]
    [InlineData("https://www.anilist.co/Manga/30013")]
    public void AMangaUrl_ResolvesToTheDetailsPage(string url)
    {
        // Was deliberately unresolvable until #12: the details page pinned Media(id:, type: ANIME),
        // so a manga id 404'd and callers toasted "not supported yet" instead. The type pin is gone
        // — media ids are unique across both types — so manga routes exactly like anime now.
        var target = AniListLinkTarget.Resolve(url);

        Assert.NotNull(target);
        Assert.Equal("media-details", target.Route);
        Assert.Equal("mediaId", target.ParameterName);
        Assert.Equal(30013, target.Id);
    }
}
