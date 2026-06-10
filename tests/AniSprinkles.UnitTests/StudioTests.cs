namespace AniSprinkles.UnitTests;

public class StudioTests
{
    [Fact]
    public void DisplayName_BlankName_FallsBackToStudio()
    {
        Assert.Equal("Studio", new Studio { Name = "  " }.DisplayName);
        Assert.Equal("Toei Animation", new Studio { Name = "Toei Animation" }.DisplayName);
    }

    [Fact]
    public void FavouritesDisplay_CompactsAndGuards()
    {
        Assert.Equal("8.7k", new Studio { Favourites = 8_730 }.FavouritesDisplay);
        Assert.True(new Studio { Favourites = 8_730 }.HasFavourites);

        Assert.Equal(string.Empty, new Studio { Favourites = 0 }.FavouritesDisplay);
        Assert.False(new Studio { Favourites = null }.HasFavourites);
    }
}
