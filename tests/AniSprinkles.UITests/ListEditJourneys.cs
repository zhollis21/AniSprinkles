namespace AniSprinkles.UITests;

/// <summary>
/// Changes to list entries, followed to where the person would look for them next. Every one writes,
/// so each runs only on fixture replay (<see cref="App.RequireFixtureReplayAsync"/>), where the
/// writes live in the app's memory (<c>MutationState</c>) and are gone at the next launch. Each that
/// passes puts back what it moved, so a second run against the same launch starts from the same
/// Library; after a failure, relaunch the app. One exception, by design for now: Sword Art Online
/// comes back to Watching at 24/25, not 0/25, because a completed title returning to Watching is
/// walked back off its cap by one episode (<c>ListEntryStatusMutations</c>, the <c>Current</c>
/// case). Whether it should reset to 0 is recorded on #222.
/// </summary>
public sealed class ListEditJourneys : Journey
{
    /// <summary>The Up Next destination a journey changed, to put back; null when untouched.</summary>
    private string? _restoreUpNextTo;

    [SetUp]
    public Task RequireFixtureReplayAsync() => App.RequireFixtureReplayAsync();

    /// <summary>
    /// Puts back the Up Next destination a journey changed, passing or not. Runs before
    /// <see cref="Journey.RestoreAsync"/>, which then puts back the Library sections.
    /// </summary>
    [TearDown]
    public async Task RestoreUpNextDestinationAsync()
    {
        if (_restoreUpNextTo is { } previous && previous != "Current")
        {
            await App.DismissPopupsAsync();
            await App.SetUpNextDestinationAsync(previous);
        }
    }

    /// <summary>
    /// The long-press menu's Move to, which every list surface routes through
    /// (<c>EntryActionCoordinator</c>), from the Library: the title leaves Watching for Paused, and
    /// back. The way back is the move #221 broke under replay, filing it under Completed.
    /// </summary>
    [Test]
    public async Task MoveTo_TakesATitleFromWatchingToPaused_AndBack()
    {
        // Romaji, as Journey's setup leaves it; "Gintama Season 4" is its English title.
        const string title = "Gintama.";

        // Watching is open from setup; Paused shut, so the moved card is laid out nowhere and
        // leaving Watching can be seen as leaving the screen.
        await SetSectionAsync("Paused", expanded: false);
        var watching = await App.ReadSectionCountAsync("Watching");
        var paused = await App.ReadSectionCountAsync("Paused");

        await App.MoveToListAsync(title, "Paused");
        // Laid-out cards only: the Manga tab has a "Gintama." too, at zero bounds once it was opened.
        await Driver.WaitForGoneAsync($"'{title}' to leave Watching", automationId: "MediaCardTitle",
            predicate: e => e.Text == title && e.WindowBounds?.Y > 0);
        await App.WaitForSectionCountAsync("Watching", watching - 1);
        await App.WaitForSectionCountAsync("Paused", paused + 1);

        await SetSectionAsync("Paused", expanded: true);
        await App.MoveToListAsync(title, "Current");
        await App.WaitForSectionCountAsync("Paused", paused);
        await App.WaitForSectionCountAsync("Watching", watching);
        await App.ResetToLibraryAsync();
        await App.ScrollLibraryToAsync($"'{title}' back under Watching", "MediaCardTitle", e => e.Text == title);
    }

    /// <summary>
    /// A status changed on the details page reaches the Library it was opened from: back from the
    /// page, the title has left Watching for Paused, and the same way back. The Library skips
    /// reloading for five minutes after a load, and before #225 nothing told it about a change made
    /// anywhere else, so both counts stayed put. Paused rather than Completed keeps the rating prompt
    /// and Up Next out of it; the journey below covers those from the details page.
    /// </summary>
    [Test]
    public async Task DetailsDropdown_Paused_MovesTheTitleBetweenLibrarySections_AndBack()
    {
        // On the test account's Watching list, and the same title in every language.
        const string title = "ONE PIECE";

        // As in the Move journey: Paused shut, so leaving Watching can be seen as leaving the screen.
        await SetSectionAsync("Paused", expanded: false);
        var watching = await App.ReadSectionCountAsync("Watching");
        var paused = await App.ReadSectionCountAsync("Paused");

        await App.OpenLibraryCardAsync(text => text == title);
        await SetDetailsStatusAsync("Watching", "Paused", "Paused");
        await Driver.PressBackAsync();
        await Driver.WaitForGoneAsync($"'{title}' to leave Watching", automationId: "MediaCardTitle",
            predicate: e => e.Text == title && e.WindowBounds?.Y > 0);
        await App.WaitForSectionCountAsync("Watching", watching - 1);
        await App.WaitForSectionCountAsync("Paused", paused + 1);

        await SetSectionAsync("Paused", expanded: true);
        await App.OpenLibraryCardAsync(text => text == title);
        await SetDetailsStatusAsync("Paused", "Current", "Watching");
        await Driver.PressBackAsync();
        await App.WaitForSectionCountAsync("Paused", paused);
        await App.WaitForSectionCountAsync("Watching", watching);
    }

    /// <summary>
    /// +1 on the details page and Back before its 1.5 s batching sends it: the Library appears before
    /// the details page is told it has been left, so it has to wait for an edit that is not in flight
    /// yet. The page models' halves of this passed alone while a phone showed the old number.
    /// Put back with -1 the same way.
    /// </summary>
    [Test]
    public async Task DetailsPlusOne_ThenBackAtOnce_ShowsOnTheLibraryCard()
    {
        const string title = "ONE PIECE";

        await App.OpenLibraryCardAsync(text => text == title);
        var shown = await Driver.WaitForAsync("the details progress", automationId: "DetailsProgress",
            predicate: e => int.TryParse(e.Text?.Split('/')[0], out _));
        var before = int.Parse(shown.Text?.Split('/')[0] ?? "0", System.Globalization.CultureInfo.InvariantCulture);

        await ChangeProgressThenLeaveAsync("ProgressPlus", before + 1);
        await App.OpenLibraryCardAsync(text => text == title);
        await ChangeProgressThenLeaveAsync("ProgressMinus", before);
    }

    private async Task ChangeProgressThenLeaveAsync(string button, int expected)
    {
        await Driver.TapAsync(button, automationId: button);
        await Driver.PressBackAsync();
        await Driver.WaitForAsync($"the Library card to read {expected}", automationId: "MediaCardProgress",
            predicate: e => e.WindowBounds?.Y > 0 && e.Text?.StartsWith($"{expected}/", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// The details page's status dropdown, from <paramref name="from"/> to <paramref name="status"/>
    /// (a <c>MediaListStatus</c> name), waiting for the label to read <paramref name="label"/>.
    /// </summary>
    private async Task SetDetailsStatusAsync(string from, string status, string label)
    {
        await Driver.WaitForAsync($"the list status to read {from}", automationId: "ListStatusLabel",
            predicate: e => e.Text == from);
        await Driver.TapAsync("the list status button", automationId: "ListStatusButton");
        await Driver.TapAsync(label, automationId: $"ListStatusOption_{status}");
        await Driver.WaitForAsync($"the list status to read {label}", automationId: "ListStatusLabel",
            predicate: e => e.Text == label);
    }

    /// <summary>
    /// #103's Up Next sheet, from the Library: Mark as completed, skip the rating, tick one related
    /// title and add it. The completed title moves to Completed and the picked one joins Watching,
    /// under the title the sheet showed. Which titles are offered, and where each goes, is
    /// <c>UpNextTests</c>' job; this is that the sheet is wired to the menu and the save reaches the
    /// Library.
    /// <para>
    /// Where a pick goes is a device setting (Settings, Up Next, Suggestions Go To), so the journey
    /// sets it to Watching and puts it back afterwards.
    /// </para>
    /// </summary>
    [Test]
    public async Task MarkAsCompleted_OffersUpNext_AndAddsThePickToWatching()
    {
        const string title = "Sword Art Online";
        await App.RequireUpNextEnabledAsync();
        _restoreUpNextTo = await App.SetUpNextDestinationAsync("Current");
        await App.ResetToLibraryAsync();
        var watching = await App.ReadSectionCountAsync("Watching");
        var completed = await App.ReadSectionCountAsync("Completed");

        await App.OpenEntryActionsAsync(title);
        await Driver.TapAsync("Mark as completed", automationId: "EntryAction_MarkCompleted");
        await Driver.TapAsync("Skip on the rating prompt", automationId: "RatingSkip");

        // Laid out, not just present: the sheet's rows are in the tree a moment before they have
        // bounds to touch.
        var pick = await Driver.WaitForAsync("a related title on the Up Next sheet", automationId: "UpNextTitle",
            predicate: e => !string.IsNullOrEmpty(e.Text) && e.WindowBounds is { Height: > 0 });
        await Driver.TouchAsync(pick, $"the '{pick.Text}' row");
        await Driver.WaitForAsync("the Up Next button to offer one add", automationId: "UpNextConfirm",
            predicate: e => e.Text?.StartsWith("Add 1", StringComparison.Ordinal) == true);
        await Driver.TapAsync("the Up Next add button", automationId: "UpNextConfirm");
        await Driver.WaitForGoneAsync("the Up Next sheet", automationId: "UpNextConfirm");

        await App.ScrollLibraryToAsync($"'{pick.Text}' under Watching", "MediaCardTitle", e => e.Text == pick.Text);
        await App.ResetToLibraryAsync();
        await App.WaitForSectionCountAsync("Watching", watching);
        await App.WaitForSectionCountAsync("Completed", completed + 1);

        // Put the Library back: the pick off the list, the completed title back to Watching.
        await App.OpenEntryActionsAsync(pick.Text ?? string.Empty);
        await Driver.TapAsync("Remove", automationId: "EntryAction_Remove");
        await Driver.TapAsync("the removal confirm", automationId: "ConfirmAccept");
        await App.WaitForSectionCountAsync("Watching", watching - 1);
        await SetSectionAsync("Completed", expanded: true);
        await App.MoveToListAsync(title, "Current");
        await App.WaitForSectionCountAsync("Completed", completed);
        await App.WaitForSectionCountAsync("Watching", watching);
    }

    /// <summary>
    /// The details page's status dropdown completes too, and offers Up Next the same way; Not now
    /// closes it with the status saved, and going back shows the title moved. Put back to Watching
    /// through the same dropdown.
    /// </summary>
    [Test]
    public async Task DetailsDropdown_Completed_OffersUpNext_AndNotNowKeepsTheCompletion()
    {
        // On the test account's Watching list, relations recorded.
        const string title = "ONE PIECE";
        await App.RequireUpNextEnabledAsync();
        await SetSectionAsync("Completed", expanded: false);
        var watching = await App.ReadSectionCountAsync("Watching");
        var completed = await App.ReadSectionCountAsync("Completed");

        await App.OpenLibraryCardAsync(text => text == title);
        await Driver.WaitForAsync("the list status to read Watching", automationId: "ListStatusLabel",
            predicate: e => e.Text == "Watching");

        await Driver.TapAsync("the list status button", automationId: "ListStatusButton");
        await Driver.TapAsync("Completed", automationId: "ListStatusOption_Completed");
        await Driver.TapAsync("Skip on the rating prompt", automationId: "RatingSkip");
        await Driver.WaitForAsync("a related title on the Up Next sheet", automationId: "UpNextTitle");
        await Driver.TapAsync("Not now", automationId: "UpNextNotNow");
        await Driver.WaitForGoneAsync("the Up Next sheet", automationId: "UpNextNotNow");
        await Driver.WaitForAsync("the list status to read Completed", automationId: "ListStatusLabel",
            predicate: e => e.Text == "Completed");

        await Driver.PressBackAsync();
        await Driver.WaitForGoneAsync($"'{title}' to leave Watching", automationId: "MediaCardTitle",
            predicate: e => e.Text == title && e.WindowBounds?.Y > 0);
        await App.WaitForSectionCountAsync("Watching", watching - 1);
        await App.WaitForSectionCountAsync("Completed", completed + 1);

        await SetSectionAsync("Completed", expanded: true);
        await App.OpenLibraryCardAsync(text => text == title);
        await SetDetailsStatusAsync("Completed", "Current", "Watching");
        await Driver.PressBackAsync();
        await App.WaitForSectionCountAsync("Completed", completed);
        await App.WaitForSectionCountAsync("Watching", watching);
    }
}
