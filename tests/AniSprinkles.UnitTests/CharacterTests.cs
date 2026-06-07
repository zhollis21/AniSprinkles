using AniSprinkles.Models;

namespace AniSprinkles.UnitTests;

public class CharacterTests
{
    // Voice Roles cards show an always-on heart bound to FavouritesOrZero, so a character with no
    // favourites must read "0" rather than blank (which would look like a broken card).
    [Theory]
    [InlineData(null, "0")]
    [InlineData(0, "0")]
    [InlineData(42, "42")]
    [InlineData(1500, "1.5k")]
    public void FavouritesOrZero_ShowsZeroWhenMissing(int? favourites, string expected)
        => Assert.Equal(expected, new Character { Favourites = favourites }.FavouritesOrZero);
}
