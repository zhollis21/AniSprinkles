namespace AniSprinkles.UITests;

/// <summary>
/// What a page shows after the person leaves its tab and comes back (#131). Pushed pages get no
/// <c>OnAppearing</c> on a tab return (AGENTS.md, "Pushed detail pages get no lifecycle on a tab
/// return"), and a page-model test cannot tell, because it calls the page model directly. These drive
/// the real app, so the only way they pass is if something on the page actually reacted.
/// </summary>
public sealed class TabReturnJourneys : IDisposable
{
    private readonly AppDriver _driver = AppDriver.Connect();
    private readonly App _app;

    public TabReturnJourneys() => _app = new App(_driver);

    /// <summary>Every journey starts signed in, on Romaji, at the Library with nothing pushed.</summary>
    [SetUp]
    public async Task StartFromLibraryAsync()
    {
        await _app.SetTitleLanguageAsync("Romaji");
        await _app.ResetToLibraryAsync();
    }

    /// <summary>
    /// #127: a details page re-renders a display setting changed on another tab. The page sits in the
    /// Library tab's backgrounded stack while Settings is current, so only <c>OnNavigatedTo</c> on the
    /// way back can refresh it.
    /// </summary>
    [Test]
    public async Task DetailsPage_ShowsTheNewTitleLanguage_AfterATabReturn()
    {
        await _driver.TapAsync("a Library card", automationId: "MediaCard");
        var before = (await _driver.WaitForAsync("the details title", automationId: "DetailsTitle",
            predicate: e => !string.IsNullOrEmpty(e.Text))).Text;

        await _app.SetTitleLanguageAsync("Native");
        await _app.GoToTabAsync("Library");

        await _driver.WaitForAsync($"the details title to change from '{before}'", automationId: "DetailsTitle",
            predicate: e => !string.IsNullOrEmpty(e.Text) && e.Text != before);
    }

    /// <summary>
    /// The reuse guard's other job: a tab return re-projects the page, it does not reload it, so the
    /// sort the person picked and how far they scrolled both survive.
    /// </summary>
    [Test]
    public async Task CharacterPage_KeepsItsSortAndScrollPosition_AcrossATabReturn()
    {
        // Luffy (AniList character 40), the character the CI screenshots already use; the fixture
        // recorder walks every appearances sort, so this one replays without a FIXTURE MISS.
        const string sort = "Avg Score";
        await _app.NavigateAsync("character-details?characterId=40");

        await _driver.TapAsync("the Appears In sort", automationId: "SortDropdown");
        await _driver.TapAsync($"the '{sort}' sort option", automationId: "SortOption_SCORE_DESC");
        await _driver.WaitForAsync($"the sort to read '{sort}'", automationId: "SortDropdownLabel",
            predicate: e => e.Text == sort);

        var list = await _driver.WaitForAsync("the Appears In list", automationId: "AppearancesList");
        Assert.That(await _driver.Client.ScrollAsync(list.Id, itemIndex: 6, scrollToPosition: "Start", animated: false), Is.True,
            "The agent could not scroll the Appears In list.");
        var firstBefore = await FirstVisibleAppearanceAsync(list);
        Assert.That(firstBefore, Is.Not.Null, "No appearance was visible after scrolling.");

        await _app.GoToTabAsync("Settings");
        await _app.GoToTabAsync("Library");

        await _driver.WaitForAsync($"the sort to still read '{sort}'", automationId: "SortDropdownLabel",
            predicate: e => e.Text == sort);
        list = await _driver.WaitForAsync("the Appears In list", automationId: "AppearancesList");
        Assert.That(await FirstVisibleAppearanceAsync(list), Is.EqualTo(firstBefore),
            "The Appears In list did not keep its scroll position across the tab return.");
    }

    /// <summary>
    /// The case that already worked, kept from regressing: a tab-root page does get
    /// <c>OnAppearing</c>, and the Library re-renders its cards in the new language.
    /// <para>
    /// The same card is compared before and after, found by where it sits on screen: any card's
    /// title differs from the first one's, so "a title changed" would pass with nothing re-rendered.
    /// </para>
    /// </summary>
    [Test]
    public async Task Library_ShowsTheNewTitleLanguage_AfterATabReturn()
    {
        await _driver.WaitForAsync("a Library card title", automationId: "MediaCardTitle",
            predicate: e => !string.IsNullOrEmpty(e.Text) && e.WindowBounds is not null);
        if (await TopLeftCardTitleAsync() is not { } before)
        {
            Assert.Fail("No Library card title was visible with window bounds.");
            return;
        }

        await _app.SetTitleLanguageAsync("Native");
        await _app.GoToTabAsync("Library");

        await _driver.WaitForAsync($"the first Library card's title to change from '{before.Text}'",
            automationId: "MediaCardTitle",
            predicate: e => before.IsAt(e) && !string.IsNullOrEmpty(e.Text) && e.Text != before.Text);
    }

    /// <summary>The title of the card nearest the top-left of the screen, with where it sits.</summary>
    private async Task<CardTitle?> TopLeftCardTitleAsync()
    {
        var titles = await _driver.Client.QueryAsync(automationId: "MediaCardTitle");
        return titles
            .Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text))
            .Select(t => t.WindowBounds is { } b ? new CardTitle(t.Text, b.X, b.Y) : null)
            .OfType<CardTitle>()
            .OrderBy(t => t.Y)
            .ThenBy(t => t.X)
            .FirstOrDefault();
    }

    /// <summary>A card title and where it sat on screen, in window units.</summary>
    private sealed record CardTitle(string? Text, double X, double Y)
    {
        /// <summary>Whether <paramref name="element"/> sits where this title did, within a unit.</summary>
        public bool IsAt(ElementInfo element) =>
            element.WindowBounds is { } b && Math.Abs(b.X - X) < 1 && Math.Abs(b.Y - Y) < 1;
    }

    /// <summary>
    /// The title of the leftmost appearance card inside the list's bounds. A recycled card view is
    /// reused for a different item, so the visible titles are what identify the position, not ids.
    /// </summary>
    private async Task<string?> FirstVisibleAppearanceAsync(ElementInfo list)
    {
        var left = list.WindowBounds?.X ?? 0;
        var titles = await _driver.Client.QueryAsync(automationId: "AppearanceTitle");
        return titles
            .Where(t => t.IsVisible)
            .Select(t => (t.Text, X: t.WindowBounds?.X))
            .Where(t => t.X >= left)
            .OrderBy(t => t.X)
            .Select(t => t.Text)
            .FirstOrDefault();
    }

    /// <summary>Leaves the app as the next journey expects to find it.</summary>
    [TearDown]
    public async Task RestoreAsync()
    {
        await _app.SetTitleLanguageAsync("Romaji");
        await _app.ResetToLibraryAsync();
    }

    public void Dispose() => _driver.Dispose();
}
