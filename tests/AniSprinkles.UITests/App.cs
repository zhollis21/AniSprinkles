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

    public AppDriver Driver { get; } = driver;

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
        var item = await Driver.WaitForAsync($"the {title} tab", text: title, type: "BottomNavigationItemView");
        await Driver.TouchAsync(item, $"the {title} tab");
    }

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
        await GoToTabAsync("Library");
        await GoToTabAsync("Library");
        await Driver.WaitForAsync("the Library cards", automationId: "MediaCard");
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
        var chipId = $"TitleLanguage{language}";

        // Display Preferences is a collapsible section; open it if this run starts with it shut.
        var chips = await Driver.Client.QueryAsync(automationId: chipId);
        if (!chips.Any(c => c.IsVisible))
        {
            await Driver.TapAsync("the Display Preferences header", automationId: "DisplayPreferencesHeader");
        }

        await Driver.TapAsync($"the {language} title-language chip", automationId: chipId);
    }
}
