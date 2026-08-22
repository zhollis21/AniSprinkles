namespace AniSprinkles.UnitTests;

/// <summary>
/// AniList returns a placeholder URL rather than null for entities with no artwork, so a plain
/// null/empty check leaves its grey "no image" graphic on screen where the app's own placeholder
/// belongs. Cheap to get wrong again, and invisible in code review.
/// </summary>
public class ImageUrlTests
{
    [Theory]
    [InlineData("https://s4.anilist.co/file/anilistcdn/character/large/b123-abc.png")]
    [InlineData("https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx1-abc.jpg")]
    [InlineData("https://example.test/not-a-default.jpg.png")]
    public void IsReal_ForAnActualImage_IsTrue(string url) => Assert.True(ImageUrl.IsReal(url));

    [Theory]
    [InlineData("https://s4.anilist.co/file/anilistcdn/character/large/default.jpg")]
    [InlineData("https://s4.anilist.co/file/anilistcdn/staff/large/DEFAULT.JPG")]
    public void IsReal_ForAniListsPlaceholder_IsFalse(string url) => Assert.False(ImageUrl.IsReal(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsReal_ForNothing_IsFalse(string? url) => Assert.False(ImageUrl.IsReal(url));
}
