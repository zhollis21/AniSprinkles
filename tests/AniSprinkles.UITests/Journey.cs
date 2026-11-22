namespace AniSprinkles.UITests;

/// <summary>
/// What every journey shares: a driver on the running app, and the state each one starts from and
/// leaves behind — signed in, on Romaji, at the Anime Library with nothing pushed and Watching open.
/// Titles are found by their Romaji text, so the language reset is what makes a journey's lookups
/// mean the same thing every run.
/// </summary>
public abstract class Journey : IDisposable
{
    /// <summary>
    /// A title that reads the same in Romaji, English and Native, so a journey watching a title
    /// change must not pick it. It is on the test account's Watching list and first on several
    /// pages: Luffy's, Mayumi Tanaka's and Toei Animation's.
    /// </summary>
    private protected const string SameInEveryLanguage = "ONE PIECE";

    /// <summary>Each Library section a journey opened or closed, with the state it was found in.</summary>
    private readonly Dictionary<string, bool> _restoreSections = [];

    private protected Journey()
    {
        Driver = AppDriver.Connect();
        App = new App(Driver);
    }

    private protected AppDriver Driver { get; }

    private protected App App { get; }

    /// <summary>
    /// Watching is opened here because most journeys need its cards, and a collapsed section has
    /// none: sections keep whatever state they were left in for the whole launch.
    /// </summary>
    [SetUp]
    public async Task StartFromLibraryAsync()
    {
        await App.DismissPopupsAsync();
        await App.SetTitleLanguageAsync("Romaji");
        await App.ResetToLibraryAsync();
        await SetSectionAsync("Watching", expanded: true);
    }

    /// <summary>
    /// Leaves the app as the next journey expects to find it, and puts back every Library section
    /// the journey opened or closed, whether it passed or not. Runs after a fixture's own teardown.
    /// </summary>
    [TearDown]
    public async Task RestoreAsync()
    {
        await App.DismissPopupsAsync();
        await App.SetTitleLanguageAsync("Romaji");
        await App.ResetToLibraryAsync();
        foreach (var (section, expanded) in _restoreSections)
        {
            await App.SetSectionExpandedAsync(section, expanded);
        }
    }

    /// <summary>
    /// Opens or closes a Library section for this journey. A journey sets the states it relies on
    /// rather than assuming the defaults, and the first change records the state it found for
    /// <see cref="RestoreAsync"/>.
    /// </summary>
    private protected async Task SetSectionAsync(string section, bool expanded)
    {
        if (!_restoreSections.ContainsKey(section))
        {
            _restoreSections[section] = await App.IsSectionExpandedAsync(section);
        }

        await App.SetSectionExpandedAsync(section, expanded);
    }

    public void Dispose()
    {
        Driver.Dispose();
        GC.SuppressFinalize(this);
    }
}
