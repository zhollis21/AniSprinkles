namespace AniSprinkles.UITests;

/// <summary>
/// How Shell's bottom tabs behave around pushed pages: things only the real app's navigation can
/// show, which were checked by hand before these existed.
/// </summary>
public sealed class NavigationJourneys : Journey
{
    /// <summary>
    /// #177: on MAUI 11's Android Shell handlers, the Library tab's ANIME / MANGA strip was redrawn,
    /// live, over a details page after a bottom-tab round trip. <c>MauiProgram</c> pins Android Shell
    /// to the legacy renderer to avoid it; this is the check AGENTS.md asks for before that pin comes
    /// out, so removing it can be judged by a CI run instead of four steps by hand.
    /// <para>
    /// Judged by where the details title sits, not by finding the strip: the strip pushed the page's
    /// content down by its own height, and MAUI 11's handlers do not show the agent their native
    /// views, so a query for the strip would find nothing on exactly the path this guards.
    /// </para>
    /// </summary>
    [Test]
    public async Task DetailsPage_HasNoTopTabStrip_AfterATabReturn()
    {
        await App.OpenLibraryCardAsync();
        var before = await SettledDetailsTitleAsync();

        await App.GoToTabAsync("Discover");
        await App.GoToTabAsync("Library");
        var after = await SettledDetailsTitleAsync();

        Assert.That(after, Is.EqualTo(before).Within(1),
            "The details page moved down after a tab return: the Library's ANIME / MANGA strip is drawn over it (#177).");
    }

    /// <summary>The details title's top once it stops moving, which it does as the page lays out.</summary>
    private async Task<double> SettledDetailsTitleAsync()
    {
        double? previous = null;
        for (var look = 0; look < 20; look++)
        {
            var title = await Driver.WaitForAsync("the details title", automationId: "DetailsTitle",
                predicate: e => e.WindowBounds is { Height: > 0 });
            var y = title.WindowBounds?.Y ?? 0;
            if (previous is { } last && Math.Abs(last - y) < 0.5)
            {
                return y;
            }

            previous = y;
            await Task.Delay(AppDriver.PollInterval);
        }

        Assert.Fail("The details title never stopped moving.");
        return 0;
    }

    /// <summary>
    /// #200: re-tapping the current tab pops a pushed page off without moving the root's scroll, and
    /// re-tapping it at the root scrolls to the top. Both are a real touch on the native tab item
    /// (<c>TabReselectShellRenderer</c>), which no unit test can reach.
    /// </summary>
    [Test]
    public async Task RetappingTheCurrentTab_PopsToTheRoot_ThenScrollsToTheTop()
    {
        // Enough to scroll in any view mode: Compact rows are short, and Watching's four cards with
        // every other section shut can fit on one screen. Setup's teardown shuts them again.
        foreach (var section in new[] { "Completed", "Planning", "Dropped", "Paused" })
        {
            await SetSectionAsync(section, expanded: true);
        }

        await App.ResetToLibraryAsync();
        var list = await App.LibraryListAsync();
        var top = await TopCardTitleAsync(list);

        await Driver.ScrollDownAsync(list, "the Library list");
        await WaitForScrolledAsync(top);

        // Where the list sits now, to find it in the same place after the pop.
        var here = await SettledTitleOnScreenAsync(list);
        await App.OpenLibraryCardAsync();

        await App.GoToTabAsync("Library");
        await Driver.WaitForGoneAsync("the details page", automationId: "DetailsTitle");
        await Driver.WaitForAsync($"'{here.Text}' where it was before the details page", automationId: "MediaCardTitle",
            predicate: e => e.Text == here.Text && e.WindowBounds is { } b && Math.Abs(b.Y - here.Y) < 1);

        await App.GoToTabAsync("Library");
        await Driver.WaitForAsync($"'{top.Text}' back at the top", automationId: "MediaCardTitle",
            predicate: e => e.Text == top.Text && IsAtTop(e, top));
    }

    /// <summary>The highest card title inside the list once the list has stopped moving.</summary>
    private async Task<(string Text, double Y)> SettledTitleOnScreenAsync(ElementInfo list)
    {
        (string Text, double Y)? previous = null;
        for (var look = 0; look < 20; look++)
        {
            var now = await HighestTitleAsync(list);
            if (now == previous && now is { } settled)
            {
                return settled;
            }

            previous = now;
            await Task.Delay(AppDriver.PollInterval);
        }

        Assert.Fail("The Library list never stopped moving, or showed no card on screen.");
        return default;
    }

    private async Task<(string Text, double Y)?> HighestTitleAsync(ElementInfo list)
    {
        if (list.WindowBounds is not { } area)
        {
            return null;
        }

        var highest = (await Driver.Client.QueryAsync(automationId: "MediaCardTitle"))
            .Where(e => e.IsVisible && !string.IsNullOrEmpty(e.Text)
                && e.WindowBounds is { } b && b.Y > area.Y && b.Y + b.Height < area.Y + area.Height)
            .MinBy(e => e.WindowBounds?.Y ?? double.MaxValue);
        return highest is { Text: { } text, WindowBounds: { } bounds } ? (text, bounds.Y) : null;
    }

    /// <summary>The highest card title inside the list (an off-screen tab's report zero bounds).</summary>
    private async Task<(string Text, double Y)> TopCardTitleAsync(ElementInfo list)
    {
        var listTop = list.WindowBounds?.Y ?? 0;
        await Driver.WaitForAsync("a Library card title", automationId: "MediaCardTitle",
            predicate: e => !string.IsNullOrEmpty(e.Text) && e.WindowBounds?.Y > listTop);
        var titles = await Driver.Client.QueryAsync(automationId: "MediaCardTitle");
        var top = titles
            .Where(e => e.IsVisible && !string.IsNullOrEmpty(e.Text) && e.WindowBounds?.Y > listTop)
            .MinBy(e => e.WindowBounds?.Y ?? double.MaxValue);
        if (top is not { Text: { } text, WindowBounds: { } bounds })
        {
            Assert.Fail("No Library card title to scroll away from.");
            return default;
        }

        return (text, bounds.Y);
    }

    private async Task WaitForScrolledAsync((string Text, double Y) top)
    {
        var deadline = DateTime.UtcNow + AppDriver.DefaultTimeout;
        while (IsAtTop(await FindTitleAsync(top.Text), top))
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "The drag did not scroll the Library list.");
            await Task.Delay(AppDriver.PollInterval);
        }
    }

    /// <summary>
    /// The laid-out card with this title. Not any visible one: the Manga tab's cards stay in the
    /// tree, reported visible at zero bounds, and its ONE PIECE would answer for the Anime one.
    /// </summary>
    private async Task<ElementInfo?> FindTitleAsync(string text)
        => (await Driver.Client.QueryAsync(automationId: "MediaCardTitle", text: text))
            .FirstOrDefault(e => e.IsVisible && e.Text == text && e.WindowBounds?.Y > 0);

    /// <summary>
    /// Where it was before any scrolling, within a pixel. A recycled card that scrolled off counts as
    /// moved, whatever it now shows.
    /// </summary>
    private static bool IsAtTop(ElementInfo? title, (string Text, double Y) top)
        => title?.WindowBounds is { } b && Math.Abs(b.Y - top.Y) < 1;
}
