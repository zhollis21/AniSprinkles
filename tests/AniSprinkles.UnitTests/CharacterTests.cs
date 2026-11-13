using AniSprinkles.Models;

namespace AniSprinkles.UnitTests;

public class CharacterTests
{
    // Voice Roles cards show an always-on heart bound to FavouritesOrZero, so a character with no
    // favourites must read "0" rather than blank (which would look like a broken card).
    [TestCase(null, "0")]
    [TestCase(0, "0")]
    [TestCase(42, "42")]
    [TestCase(1500, "1.5k")]
    public void FavouritesOrZero_ShowsZeroWhenMissing(int? favourites, string expected)
        => Assert.That(new Character { Favourites = favourites }.FavouritesOrZero, Is.EqualTo(expected));
}
