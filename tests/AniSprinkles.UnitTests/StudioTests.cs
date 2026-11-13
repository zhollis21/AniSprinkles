namespace AniSprinkles.UnitTests;

public class StudioTests
{
    [Test]
    public void DisplayName_BlankName_FallsBackToStudio()
    {
        Assert.That(new Studio { Name = "  " }.DisplayName, Is.EqualTo("Studio"));
        Assert.That(new Studio { Name = "Toei Animation" }.DisplayName, Is.EqualTo("Toei Animation"));
    }

    [Test]
    public void FavouritesDisplay_CompactsAndGuards()
    {
        Assert.That(new Studio { Favourites = 8_730 }.FavouritesDisplay, Is.EqualTo("8.7k"));
        Assert.That(new Studio { Favourites = 8_730 }.HasFavourites, Is.True);

        Assert.That(new Studio { Favourites = 0 }.FavouritesDisplay, Is.EqualTo(string.Empty));
        Assert.That(new Studio { Favourites = null }.HasFavourites, Is.False);
    }
}
