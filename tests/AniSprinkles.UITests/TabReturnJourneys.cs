namespace AniSprinkles.UITests;

/// <summary>
/// What a page shows after the person leaves its tab and comes back (#131). Pushed pages get no
/// <c>OnAppearing</c> on a tab return (AGENTS.md, "Pushed detail pages get no lifecycle on a tab
/// return"), and a page-model test cannot tell, because it calls the page model directly. These drive
/// the real app, so the only way they pass is if something on the page actually reacted.
/// </summary>
public sealed class TabReturnJourneys : Journey
{
    /// <summary>A tab other than the Library that a journey pushed a page onto, to pop again.</summary>
    private string? _resetTab;

    /// <summary>
    /// Pops what a journey pushed onto another tab, whose titles would otherwise sit, reported
    /// visible, under the same ids as the Library's. Runs before <see cref="Journey.RestoreAsync"/>.
    /// </summary>
    [TearDown]
    public async Task PopOtherTabAsync()
    {
        if (_resetTab is { } tab)
        {
            await App.DismissPopupsAsync();
            await App.ResetToTabAsync(tab);
        }
    }

    /// <summary>
    /// #127: a details page re-renders a display setting changed on another tab. The page sits in the
    /// Library tab's backgrounded stack while Settings is current, so only <c>OnNavigatedTo</c> on the
    /// way back can refresh it.
    /// </summary>
    [Test]
    public async Task DetailsPage_ShowsTheNewTitleLanguage_AfterATabReturn()
    {
        await App.OpenLibraryCardAsync(title => title != SameInEveryLanguage);
        var before = (await Driver.WaitForAsync("the details title", automationId: "DetailsTitle",
            predicate: e => !string.IsNullOrEmpty(e.Text))).Text;

        await App.SetTitleLanguageAsync("Native");
        await App.GoToTabAsync("Library");

        await Driver.WaitForAsync($"the details title to change from '{before}'", automationId: "DetailsTitle",
            predicate: e => !string.IsNullOrEmpty(e.Text) && e.Text != before);
    }

    /// <summary>
    /// The same as <see cref="DetailsPage_ShowsTheNewTitleLanguage_AfterATabReturn"/> for the other
    /// pushed pages, each reading a media title it shows: #133 checked these by hand. The label is
    /// followed by element id, as on the Library, because each page shows many titles and every
    /// other one already differs from the first.
    /// <para>
    /// The ids are what the fixtures recorded: Luffy (character 40), Mayumi Tanaka (staff 95075) and
    /// Toei Animation (studio 18), all CI screenshot subjects, and View All for Trending Now.
    /// </para>
    /// </summary>
    [TestCase("Library", "character-details?characterId=40", "AppearanceTitle", TestName = "CharacterPage_ShowsTheNewTitleLanguage_AfterATabReturn")]
    [TestCase("Library", "staff-details?staffId=95075", "VoiceRoleMediaTitle", TestName = "StaffPage_ShowsTheNewTitleLanguage_AfterATabReturn")]
    [TestCase("Library", "studio-details?studioId=18", "StudioMediaTitle", TestName = "StudioPage_ShowsTheNewTitleLanguage_AfterATabReturn")]
    [TestCase("Discover", "media-browse?section=Trending", "MediaCardTitle", TestName = "ViewAll_ShowsTheNewTitleLanguage_AfterATabReturn")]
    public async Task PushedPage_ShowsTheNewTitleLanguage_AfterATabReturn(string tab, string route, string titleId)
    {
        // The Library's cards stay in the tree, reported visible, while another tab is current, and
        // View All's titles share their id. So a title there is one the Library does not have.
        var elsewhere = (await Driver.Client.QueryAsync(automationId: titleId)).Select(e => e.Id).ToHashSet();
        if (tab != "Library")
        {
            _resetTab = tab;
            await App.ResetToTabAsync(tab);
        }

        await App.NavigateAsync(route);
        var before = await Driver.WaitForAsync("a media title on the page", automationId: titleId,
            predicate: e => !string.IsNullOrEmpty(e.Text) && e.Text != SameInEveryLanguage && !elsewhere.Contains(e.Id));

        await App.SetTitleLanguageAsync("Native");
        await App.GoToTabAsync(tab);

        await Driver.WaitForAsync($"the title that read '{before.Text}' to change", automationId: titleId,
            predicate: e => e.Id == before.Id && !string.IsNullOrEmpty(e.Text) && e.Text != before.Text);
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
        await App.NavigateAsync("character-details?characterId=40");

        await Driver.TapAsync("the Appears In sort", automationId: "SortDropdown");
        await Driver.TapAsync($"the '{sort}' sort option", automationId: "SortOption_SCORE_DESC");
        await Driver.WaitForAsync($"the sort to read '{sort}'", automationId: "SortDropdownLabel",
            predicate: e => e.Text == sort);

        var list = await Driver.WaitForAsync("the Appears In list", automationId: "AppearancesList");
        Assert.That(await Driver.Client.ScrollAsync(list.Id, itemIndex: 6, scrollToPosition: "Start", animated: false), Is.True,
            "The agent could not scroll the Appears In list.");
        var firstBefore = await FirstVisibleAppearanceAsync(list);
        Assert.That(firstBefore, Is.Not.Null, "No appearance was visible after scrolling.");

        await App.GoToTabAsync("Settings");
        await App.GoToTabAsync("Library");

        await Driver.WaitForAsync($"the sort to still read '{sort}'", automationId: "SortDropdownLabel",
            predicate: e => e.Text == sort);
        list = await Driver.WaitForAsync("the Appears In list", automationId: "AppearancesList");
        Assert.That(await FirstVisibleAppearanceAsync(list), Is.EqualTo(firstBefore),
            "The Appears In list did not keep its scroll position across the tab return.");
    }

    /// <summary>
    /// The case that already worked, kept from regressing: a tab-root page does get
    /// <c>OnAppearing</c>, and the Library re-renders its cards in the new language.
    /// <para>
    /// The same title label is compared before and after, by the agent's element id, which a tab
    /// return keeps (checked 2026-11-21). Not "any title changed": every other card's title differs
    /// from the first one's, so that would pass with nothing re-rendered. Not its position either: the
    /// Standard card centres its text, so a title that wraps differently in Native moves the label.
    /// The id names the label, not the media, which is enough here: nothing scrolls, and a Title sort
    /// only reorders the cards inside the same re-render this journey is checking for.
    /// </para>
    /// </summary>
    [Test]
    public async Task Library_ShowsTheNewTitleLanguage_AfterATabReturn()
    {
        // On screen: the Manga tab's titles share the id once it has been opened.
        var before = await App.ScrollLibraryToAsync("a Library card title", "MediaCardTitle",
            e => !string.IsNullOrEmpty(e.Text) && e.Text != SameInEveryLanguage);

        await App.SetTitleLanguageAsync("Native");
        await App.GoToTabAsync("Library");

        await Driver.WaitForAsync($"the title that read '{before.Text}' to change", automationId: "MediaCardTitle",
            predicate: e => e.Id == before.Id && !string.IsNullOrEmpty(e.Text) && e.Text != before.Text);
    }

    /// <summary>
    /// The title of the leftmost appearance card inside the list's bounds. A recycled card view is
    /// reused for a different item, so the visible titles are what identify the position, not ids.
    /// </summary>
    private async Task<string?> FirstVisibleAppearanceAsync(ElementInfo list)
    {
        var left = list.WindowBounds?.X ?? 0;
        var titles = await Driver.Client.QueryAsync(automationId: "AppearanceTitle");
        return titles
            .Where(t => t.IsVisible)
            .Select(t => (t.Text, X: t.WindowBounds?.X))
            .Where(t => t.X >= left)
            .OrderBy(t => t.X)
            .Select(t => t.Text)
            .FirstOrDefault();
    }
}
