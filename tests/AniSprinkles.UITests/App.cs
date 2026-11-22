using System.Globalization;

namespace AniSprinkles.UITests;

/// <summary>
/// Moves through the app the way a person does — bottom tabs, Settings chips — so each journey reads
/// as the steps it takes. Every method waits for the screen it needs rather than assuming it is there.
/// </summary>
internal sealed class App(AppDriver driver)
{
    /// <summary>
    /// The AniList account the CI fixtures were recorded against (<c>ViewerFull</c> fixture). A journey
    /// changes settings, and the app saves them to whoever is signed in, so the suite only ever runs
    /// signed in as this account: a CI build (fixture replay, nothing leaves the device) or a Debug
    /// build signed in to the test account on purpose. A Debug run against the real account leaves
    /// its title language on Romaji, whatever it started on: the journeys reset to that and do not
    /// restore the original, since this account exists for them and CI saves nothing at all.
    /// </summary>
    public const string TestAccountName = "AniSprinkles";

    /// <summary>The preference a Debug build writes at every launch (<c>MauiProgram.UITestsFixtureReplayKey</c>).</summary>
    private const string FixtureReplayPreference = "uitests_fixture_replay";

    public AppDriver Driver { get; } = driver;

    /// <summary>
    /// Skips the journey unless the app replays recorded AniList data (<c>-p:CiBuild=true</c>), for
    /// journeys that move, complete or add titles. On a live build those writes reach the test
    /// account, which is also what the CI fixtures are recorded from, and a completion cannot be
    /// undone: AniList keeps its completed date, and Up Next adds titles. Settings changes are
    /// exempt; the title-language reset puts them back.
    /// </summary>
    public async Task RequireFixtureReplayAsync()
    {
        // {"key":…,"value":true,"type":"bool","exists":true}; anything but a true value, a missing
        // preference included (a build from before it existed), counts as live.
        var replay = await Driver.Client.GetPreferenceAsync(FixtureReplayPreference, "bool", null);
        var isReplay = replay.ValueKind == JsonValueKind.Object
            && replay.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.True;
        if (!isReplay)
        {
            Assert.Ignore("Changes list data, so it runs only on a -p:CiBuild=true build, where writes replay locally "
                + $"and never reach the test account (preference '{FixtureReplayPreference}' read {replay.GetRawText()}).");
        }
    }

    /// <summary>
    /// Skips the journey if Up Next is turned off on this device (Settings, Up Next, Suggest Related
    /// Entries; <c>UpNextPreferences</c>, on by default). Off, completing a title correctly offers
    /// nothing, so a journey waiting for the sheet would time out with the title left completed.
    /// Call it before the journey changes anything.
    /// </summary>
    public async Task RequireUpNextEnabledAsync()
    {
        // Unset means on. The agent reports an unset bool as {"exists":false,"value":false}, so the
        // value alone would read a fresh install as turned off.
        var enabled = await Driver.Client.GetPreferenceAsync("up_next_enabled", "bool", null);
        if (enabled.TryGetProperty("exists", out var exists) && exists.ValueKind == JsonValueKind.True
            && enabled.TryGetProperty("value", out var on) && on.ValueKind == JsonValueKind.False)
        {
            Assert.Ignore("Up Next is turned off in Settings on this device.");
        }
    }

    /// <summary>
    /// Points Up Next at a list (<c>Current</c> or <c>Planning</c>) through its Settings chips, and
    /// returns where it pointed before, so the journey can put it back. Device-local, not an AniList
    /// setting (<c>UpNextPreferences</c>), so it is read straight from the app's preferences: the
    /// chips expose no selected state to read (#226). Up Next must be on; see
    /// <see cref="RequireUpNextEnabledAsync"/>.
    /// </summary>
    public async Task<string> SetUpNextDestinationAsync(string status)
    {
        var stored = await Driver.Client.GetPreferenceAsync("up_next_status", "string", null);
        var previous = stored.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } named
                ? named
                : "Current";

        await GoToTabAsync("Settings");
        await OpenSettingsSectionAsync("UpNext", "Up Next");
        await Driver.TapAsync($"the Up Next {status} chip", automationId: $"UpNextDestination_{status}");
        return previous;
    }

    /// <summary>
    /// The count on a Library section's header (<c>Watching</c>, <c>Completed</c>, …), scrolling the
    /// header on screen first.
    /// </summary>
    public async Task<int> ReadSectionCountAsync(string section)
    {
        var count = await OnSectionRowAsync(section, "LibrarySectionCount", "count", e => int.TryParse(e.Text, out _));
        return int.TryParse(count.Text, out var value) ? value : -1;
    }

    /// <summary>
    /// Whether a Library section is open, read from its header's chevron, which a section's
    /// <c>IsExpanded</c> turns to 180°. Sections keep their state across reloads for the whole
    /// launch, so a journey cannot assume the defaults.
    /// </summary>
    public async Task<bool> IsSectionExpandedAsync(string section)
    {
        var chevron = await OnSectionRowAsync(section, "LibrarySectionChevron", "chevron", _ => true);
        var rotation = await Driver.Client.GetPropertyAsync(chevron.Id, "Rotation");
        return double.TryParse(rotation, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)
            && Math.Abs(degrees - 180) < 1;
    }

    /// <summary>Opens or closes a Library section, touching its header only if it is the other way.</summary>
    public async Task SetSectionExpandedAsync(string section, bool expanded)
    {
        if (await IsSectionExpandedAsync(section) == expanded)
        {
            return;
        }

        var header = await ScrollLibraryToAsync($"the {section} header", "LibrarySectionTitle", e => e.Text == section);
        await Driver.TouchAsync(header, $"the {section} header");

        var deadline = DateTime.UtcNow + AppDriver.DefaultTimeout;
        while (await IsSectionExpandedAsync(section) != expanded)
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline),
                $"The {section} section did not {(expanded ? "open" : "close")}.");
            await Task.Delay(AppDriver.PollInterval);
        }
    }

    /// <summary>
    /// A label on a section's header row. The count and the chevron carry no section name, so the
    /// one on the same row as the section's title, scrolled on screen first, is the section's.
    /// </summary>
    private async Task<ElementInfo> OnSectionRowAsync(string section, string automationId, string what, Func<ElementInfo, bool> predicate)
    {
        var title = await ScrollLibraryToAsync($"the {section} section header", "LibrarySectionTitle", e => e.Text == section);
        var row = title.WindowBounds;
        return await Driver.WaitForAsync($"the {section} section {what}", automationId: automationId,
            predicate: e => row is not null && e.WindowBounds is { } b
                && Math.Abs((b.Y + (b.Height / 2)) - (row.Y + (row.Height / 2))) < row.Height
                && predicate(e));
    }

    /// <summary>Waits until a Library section's header reads <paramref name="expected"/>.</summary>
    public async Task WaitForSectionCountAsync(string section, int expected)
    {
        var deadline = DateTime.UtcNow + AppDriver.DefaultTimeout;
        int last;
        while ((last = await ReadSectionCountAsync(section)) != expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(AppDriver.PollInterval);
        }

        Assert.That(last, Is.EqualTo(expected), $"The {section} section count.");
    }

    /// <summary>
    /// Long-presses a Library card by its title, as a person does, and waits for the action menu.
    /// The press lands on the title label; the card's long-press recognizer still sees it, since a
    /// child with no gesture of its own passes the touch up.
    /// </summary>
    public async Task OpenEntryActionsAsync(string title)
    {
        var card = await ScrollLibraryToAsync($"the '{title}' card", "MediaCardTitle", e => e.Text == title);
        await Driver.LongPressAsync(card, $"the '{title}' card");
        await Driver.WaitForAsync("the entry action menu", automationId: "EntryAction_MoveToList");
    }

    /// <summary>
    /// Scrolls the Library with real drags until a matching element sits on screen inside the list,
    /// and returns it there. The agent reports realized views as visible even when scrolled out of
    /// the list's bounds, so on screen is judged from the bounds, not from <c>IsVisible</c>.
    /// </summary>
    public async Task<ElementInfo> ScrollLibraryToAsync(string what, string automationId, Func<ElementInfo, bool> predicate)
    {
        var list = await LibraryListAsync();
        if (await SettledOnScreenAsync(automationId, predicate, list) is { } here)
        {
            return here;
        }

        // Search down from the top, which a re-tap at the root scrolls to (#200), so where the list
        // happened to be left does not matter.
        await GoToTabAsync("Library");
        await Task.Delay(PopAnimation);
        for (var drag = 0; drag < 4; drag++)
        {
            if (await SettledOnScreenAsync(automationId, predicate, list) is { } found)
            {
                return found;
            }

            await Driver.ScrollDownAsync(list, "the Library list");
        }

        if (await SettledOnScreenAsync(automationId, predicate, list) is { } last)
        {
            return last;
        }

        Assert.Fail($"Four drags down the Library from the top did not bring {what} on screen.");
        return list;
    }

    /// <summary>
    /// Opens a Library card on screen with a real touch on its title, and returns that title. Not
    /// the agent's tap on the first card it lists, which can be a Manga card off screen.
    /// </summary>
    public async Task<string> OpenLibraryCardAsync(Func<string, bool>? accept = null)
    {
        var title = await ScrollLibraryToAsync("a Library card", "MediaCardTitle",
            e => e.Text is { Length: > 0 } text && (accept?.Invoke(text) ?? true));
        await Driver.TouchAsync(title, $"the '{title.Text}' card");
        await Driver.WaitForAsync("the details title", automationId: "DetailsTitle");
        return title.Text ?? string.Empty;
    }

    /// <summary>
    /// The Anime list on screen. Once the Manga tab has been opened — CI's screenshot pass opens it —
    /// its list and headers stay in the tree, reported visible with zero bounds, under the same ids
    /// ("Completed" included), so the on-screen one is the one laid out below the top of the window.
    /// </summary>
    public Task<ElementInfo> LibraryListAsync()
        => Driver.WaitForAsync("the Library list", automationId: "LibraryList",
            predicate: e => e.WindowBounds is { Y: > 0, Height: > 0 });

    /// <summary>
    /// The matching element once it is on screen and has stopped moving, or null if it is not on
    /// screen. Stopped matters: a touch on a list still settling from a drag or a scroll to the top
    /// lands somewhere else, and a long press on a moving list is no long press at all.
    /// </summary>
    private async Task<ElementInfo?> SettledOnScreenAsync(string automationId, Func<ElementInfo, bool> predicate, ElementInfo list)
    {
        ElementInfo? previous = null;
        for (var look = 0; look < 12; look++)
        {
            var match = (await Driver.Client.QueryAsync(automationId: automationId))
                .FirstOrDefault(e => e.IsVisible && predicate(e) && IsInside(e, list));
            if (match is null)
            {
                return null;
            }

            if (previous?.WindowBounds is { } before && match.WindowBounds is { } now
                && previous.Id == match.Id && Math.Abs(before.Y - now.Y) < 0.5)
            {
                return match;
            }

            previous = match;
            await Task.Delay(AppDriver.PollInterval);
        }

        return null;
    }


    /// <summary>Long-press menu → Move to → <paramref name="status"/> (a <c>MediaListStatus</c> name).</summary>
    public async Task MoveToListAsync(string title, string status)
    {
        await OpenEntryActionsAsync(title);
        await Driver.TapAsync("Move to", automationId: "EntryAction_MoveToList");
        await Driver.TapAsync($"the {status} list", automationId: $"ListStatusOption_{status}");
    }

    private static bool IsInside(ElementInfo element, ElementInfo container)
        => element.WindowBounds is { } e && container.WindowBounds is { } c
            && e.Y + (e.Height / 2) > c.Y && e.Y + (e.Height / 2) < c.Y + c.Height;

    /// <summary>
    /// Touches a bottom-navigation tab by its title (<c>Library</c>, <c>Settings</c>, …): finds the
    /// native Material <c>BottomNavigationItemView</c> through the agent, then touches its centre
    /// through the device (<see cref="AppDriver.TouchAsync"/>).
    /// <para>
    /// Never the agent's own tap. On the agent's <c>Tab_&lt;route&gt;</c> marker (a
    /// <c>ShellTabMarker</c>, which "cannot receive touches") and on the native item alike, it
    /// invokes the tab's action directly, and on the way back the pushed page gets no
    /// <c>OnNavigatedTo</c>: verified 2026-11-20 from a cold start, where a details page stayed in
    /// Romaji after the agent's taps and turned Japanese after a real touch. A journey switching tabs
    /// the agent's way could not see the bug it exists to catch.
    /// </para>
    /// </summary>
    public async Task GoToTabAsync(string title)
    {
        await Driver.WaitForAsync($"the {title} tab", text: title, predicate: IsBottomTab);
        var native = (await Driver.Client.QueryAsync(type: "BottomNavigationItemView", text: title))
            .FirstOrDefault(e => e.IsVisible && IsBottomTab(e));
        if (native is not null)
        {
            await Driver.TouchAsync(native, $"the {title} tab");
            return;
        }

        // MAUI 11's handlers: every Tab reports the whole bar. Material lays its items out at equal
        // widths in Shell's order, which is the order the agent lists the Tabs in.
        var tabs = (await Driver.Client.QueryAsync(type: "Tab")).Where(e => e.IsVisible && IsBottomTab(e)).ToList();
        var index = tabs.FindIndex(e => e.Text == title);
        if (index < 0 || tabs[index].WindowBounds is not { } bar)
        {
            Assert.Fail($"The {title} tab is not in the bottom bar.");
            return;
        }

        await Driver.TouchAtAsync(bar.X + ((index + 0.5) * bar.Width / tabs.Count), bar.Y + (bar.Height / 2), $"the {title} tab");
    }

    /// <summary>
    /// A bottom-navigation item, on either Android Shell. The legacy renderer the app is pinned to
    /// (#177) shows the agent its native <c>BottomNavigationItemView</c>s; MAUI 11's Shell handlers
    /// show only the MAUI <c>Tab</c>s, each with the whole bar's bounds. Both, so the journeys still
    /// run, and #177's can judge, the day the pin comes out.
    /// </summary>
    private static bool IsBottomTab(ElementInfo e)
        => e.Type is "BottomNavigationItemView" or "Tab" && e.WindowBounds is { Height: > 0 };

    /// <summary>Shell navigation to a pushed route on the current tab, as a deep link would do it.</summary>
    public async Task NavigateAsync(string route) =>
        Assert.That(await Driver.Client.NavigateAsync(route), Is.True, $"The agent could not navigate to {route}.");

    /// <summary>
    /// Back to the Library with nothing pushed, so a page left by an earlier journey cannot cover the
    /// cards: the first touch selects the tab, the second re-selects it, which pops Shell's stack to
    /// the root, as it does for a person.
    /// <para>
    /// Not an absolute <c>//library/anime</c> navigation. After one, a page pushed onto that tab got
    /// no <c>OnNavigatedTo</c> on its next tab return (verified 2026-11-20), so every later journey
    /// would fail for a reason the app itself never meets — it has no absolute navigation at all
    /// (<c>AppShell.xaml</c>).
    /// </para>
    /// </summary>
    public async Task ResetToLibraryAsync()
    {
        await ResetToTabAsync("Library");

        // The list, not a card: with every section collapsed there are no cards to wait for.
        await LibraryListAsync();

        // After the pop's animation, because a re-tap during one is dropped (TabReselect).
        await Task.Delay(PopAnimation);
        await SelectAnimeAsync();

        // A third touch at the root scrolls it to the top (#200), which the second does not do when it
        // pops a page: journeys read section headers and the top card, which a scrolled list may have
        // recycled.
        await GoToTabAsync("Library");
        await Driver.WaitForAsync("the Library's first section header", automationId: "LibrarySectionTitle",
            predicate: e => e.WindowBounds?.Y > 0);
    }

    /// <summary>
    /// Selects the Library's Anime sub-tab with a real touch. Re-tapping the bottom tab pops and
    /// scrolls but never changes the sub-tab, so a run that starts on Manga would otherwise test
    /// the Manga list. The ANIME / MANGA strip is a native <c>TabLayout</c> whose tabs the agent
    /// lists as <c>TabView</c>s without text; Anime is the leftmost one on screen. MAUI 11's Shell
    /// handlers show the agent no native views (#177), so there the step is skipped.
    /// </summary>
    private async Task SelectAnimeAsync()
    {
        var anime = (await Driver.Client.QueryAsync(type: "TabView"))
            .Where(e => e.IsVisible && e.WindowBounds is { Y: > 0, Width: > 0 })
            .MinBy(e => e.WindowBounds?.X ?? double.MaxValue);
        if (anime is null)
        {
            return;
        }

        await Driver.TouchAsync(anime, "the Anime tab");
        await Task.Delay(PopAnimation);
    }

    /// <summary>Longer than Shell's pop-to-root animation on the emulator.</summary>
    private static readonly TimeSpan PopAnimation = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Closes whatever a failed journey left open — a sheet, a menu, a confirm — with the device's
    /// Back key, until the bottom tabs answer the agent again. While a popup is up the agent sees
    /// only its window, so without this one failure would fail every journey after it. Back only
    /// while the tabs are missing, so a pushed page is never popped by it.
    /// </summary>
    public async Task DismissPopupsAsync()
    {
        for (var press = 0; press < 3; press++)
        {
            var tabs = await Driver.Client.QueryAsync(text: "Library");
            if (tabs.Any(t => t.IsVisible && IsBottomTab(t)))
            {
                return;
            }

            await Driver.PressBackAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>The tab's root, reached the way <see cref="ResetToLibraryAsync"/> reaches the Library's.</summary>
    public async Task ResetToTabAsync(string title)
    {
        await GoToTabAsync(title);
        await GoToTabAsync(title);
    }

    /// <summary>The name on the Settings profile hero; switches to Settings to read it.</summary>
    public async Task<string?> ReadProfileNameAsync()
    {
        await GoToTabAsync("Settings");
        var name = await Driver.WaitForAsync("the Settings profile name", automationId: "ProfileName",
            predicate: e => !string.IsNullOrEmpty(e.Text));
        return name.Text;
    }

    /// <summary>
    /// Picks a title language on Settings. The choice reaches <c>AppSettings</c> as the chip is
    /// tapped (<c>SettingsPageModel.OnSelectedTitleLanguageChanged</c>), before the auto-save.
    /// </summary>
    public async Task SetTitleLanguageAsync(string language)
    {
        await GoToTabAsync("Settings");
        await OpenSettingsSectionAsync("DisplayPreferences", "Display Preferences");
        await Driver.TapAsync($"the {language} title-language chip", automationId: $"TitleLanguage{language}");
    }

    /// <summary>
    /// Opens a collapsible Settings section (<c>&lt;section&gt;Header</c> over
    /// <c>&lt;section&gt;Content</c>) if it is shut; a collapse lasts for the launch.
    /// <para>
    /// Judged by the content border, whose own <c>IsVisible</c> is what the section binds. The
    /// controls inside keep theirs true, and their last bounds, while the section is shut, so asking
    /// whether a chip is visible cannot tell (checked on device 2026-11-21).
    /// </para>
    /// </summary>
    private async Task OpenSettingsSectionAsync(string section, string what)
    {
        var header = $"{section}Header";
        await Driver.WaitForAsync($"the {what} header", automationId: header);
        if (await IsSettingsSectionOpenAsync(section))
        {
            return;
        }

        await Driver.TapAsync($"the {what} header", automationId: header);
        var deadline = DateTime.UtcNow + AppDriver.DefaultTimeout;
        while (!await IsSettingsSectionOpenAsync(section))
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), $"The {what} section did not open.");
            await Task.Delay(AppDriver.PollInterval);
        }
    }

    private async Task<bool> IsSettingsSectionOpenAsync(string section)
        => (await Driver.Client.QueryAsync(automationId: $"{section}Content")).Any(e => e.IsVisible);
}
