namespace AniSprinkles.UITests;

/// <summary>
/// Changes to list entries, followed to where the person would look for them next. Every one writes,
/// so each runs only on fixture replay (<see cref="App.RequireFixtureReplayAsync"/>), where the
/// writes live in the app's memory (<c>MutationState</c>) and are gone at the next launch. Each that
/// passes puts back what it moved, so a second run against the same launch starts from the same
/// Library; after a failure, relaunch the app.
/// </summary>
public sealed class ListEditJourneys : Journey
{
    /// <summary>The Up Next destination a journey changed, to put back; null when untouched.</summary>
    private string? _restoreUpNextTo;

    [SetUp]
    public Task RequireFixtureReplayAsync() => App.RequireFixtureReplayAsync();

    /// <summary>Runs before <see cref="Journey.RestoreAsync"/>, which then returns to the Library.</summary>
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
        var watching = await App.ReadSectionCountAsync("Watching");
        var paused = await App.ReadSectionCountAsync("Paused");

        await App.MoveToListAsync(title, "Paused");
        // Laid-out cards only: the Manga tab has a "Gintama." too, at zero bounds once it was opened.
        await Driver.WaitForGoneAsync($"'{title}' to leave Watching", automationId: "MediaCardTitle",
            predicate: e => e.Text == title && e.WindowBounds?.Y > 0);
        await App.WaitForSectionCountAsync("Watching", watching - 1);
        await App.WaitForSectionCountAsync("Paused", paused + 1);

        await App.ToggleSectionAsync("Paused");
        await App.MoveToListAsync(title, "Current");
        await App.WaitForSectionCountAsync("Paused", paused);
        await App.ToggleSectionAsync("Paused");
        await App.WaitForSectionCountAsync("Watching", watching);
        await App.ResetToLibraryAsync();
        await App.ScrollLibraryToAsync($"'{title}' back under Watching", "MediaCardTitle", e => e.Text == title);
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
        await App.ToggleSectionAsync("Completed");
        await App.MoveToListAsync(title, "Current");
        await App.WaitForSectionCountAsync("Completed", completed);
        await App.ToggleSectionAsync("Completed");
        await App.WaitForSectionCountAsync("Watching", watching);
    }

    /// <summary>
    /// The details page's status dropdown completes too, and offers Up Next the same way; Not now
    /// closes it with the status saved. Put back to Watching through the same dropdown.
    /// </summary>
    [Test]
    public async Task DetailsDropdown_Completed_OffersUpNext_AndNotNowKeepsTheCompletion()
    {
        // ONE PIECE (media 21): on the test account's Watching list, relations recorded.
        await App.NavigateAsync("media-details?mediaId=21");
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

        await Driver.TapAsync("the list status button", automationId: "ListStatusButton");
        await Driver.TapAsync("Watching", automationId: "ListStatusOption_Current");
        await Driver.WaitForAsync("the list status to read Watching again", automationId: "ListStatusLabel",
            predicate: e => e.Text == "Watching");
    }
}
