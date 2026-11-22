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

    /// <summary>The Library search box and the text setup cleared from it, to put back.</summary>
    private (string Id, string Text)? _restoreFilter;

    private protected Journey()
    {
        Driver = AppDriver.Connect();
        App = new App(Driver);
    }

    private protected AppDriver Driver { get; }

    private protected App App { get; }

    /// <summary>
    /// The Library's search text and its sections keep whatever state they were left in for the
    /// whole launch, so setup clears a filter, which hides titles and turns a section's count into
    /// "1/4", and opens Watching, whose cards most journeys need.
    /// </summary>
    [SetUp]
    public async Task StartFromLibraryAsync()
    {
        await App.DismissPopupsAsync();
        await App.SetTitleLanguageAsync("Romaji");
        await App.ResetToLibraryAsync();
        await ClearLibraryFilterAsync();
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

        // Last, so the section counts above were read unfiltered.
        if (_restoreFilter is { } filter)
        {
            await Driver.Client.FillAsync(filter.Id, filter.Text);
        }
    }

    /// <summary>
    /// Empties the Anime Library's search box if it holds text, remembering it for
    /// <see cref="RestoreAsync"/>. The laid-out box only: the Manga tab's sits at zero bounds once
    /// that tab has been opened. A hidden box holds no text, since closing the search clears it.
    /// </summary>
    private async Task ClearLibraryFilterAsync()
    {
        var box = (await Driver.Client.QueryAsync(automationId: "LibrarySearchEntry"))
            .FirstOrDefault(e => e.IsVisible && e.WindowBounds is { Y: > 0, Height: > 0 } && !string.IsNullOrEmpty(e.Text));
        if (box is not { Text: { } text })
        {
            return;
        }

        _restoreFilter = (box.Id, text);
        await Driver.Client.ClearAsync(box.Id);
        await Driver.WaitForAsync("the Library search box to empty", automationId: "LibrarySearchEntry",
            predicate: e => e.Id == box.Id && string.IsNullOrEmpty(e.Text));
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
