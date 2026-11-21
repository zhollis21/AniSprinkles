namespace AniSprinkles.UITests;

/// <summary>
/// What every journey shares: a driver on the running app, and the state each one starts from and
/// leaves behind — signed in, on Romaji, at the Library with nothing pushed. Titles are found by
/// their Romaji text, so the language reset is what makes a journey's lookups mean the same thing
/// every run.
/// </summary>
public abstract class Journey : IDisposable
{
    /// <summary>
    /// A title that reads the same in Romaji, English and Native, so a journey watching a title
    /// change must not pick it. It is on the test account's Watching list and first on several
    /// pages: Luffy's, Mayumi Tanaka's and Toei Animation's.
    /// </summary>
    private protected const string SameInEveryLanguage = "ONE PIECE";

    private protected Journey()
    {
        Driver = AppDriver.Connect();
        App = new App(Driver);
    }

    private protected AppDriver Driver { get; }

    private protected App App { get; }

    [SetUp]
    public async Task StartFromLibraryAsync()
    {
        await App.DismissPopupsAsync();
        await App.SetTitleLanguageAsync("Romaji");
        await App.ResetToLibraryAsync();
    }

    /// <summary>Leaves the app as the next journey expects to find it.</summary>
    [TearDown]
    public async Task RestoreAsync()
    {
        await App.DismissPopupsAsync();
        await App.SetTitleLanguageAsync("Romaji");
        await App.ResetToLibraryAsync();
    }

    public void Dispose()
    {
        Driver.Dispose();
        GC.SuppressFinalize(this);
    }
}
