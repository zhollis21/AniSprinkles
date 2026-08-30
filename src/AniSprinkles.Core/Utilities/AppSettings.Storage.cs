namespace AniSprinkles.Utilities;

public static partial class AppSettings
{
    /// <summary>
    /// Public because the airing worker reads this preference directly (#141). It cannot come
    /// through <see cref="Storage"/> — that is <c>internal</c>, and the worker lives in the app
    /// project — nor through <see cref="TitleLanguage"/>, which is only populated once
    /// <see cref="Load"/> has run, and the worker can execute before the app has ever launched.
    /// </summary>
    public const string TitleLanguageKey = "title_language";
    private const string ScoreFormatKey = "score_format";
    private const string DisplayAdultContentKey = "display_adult_content";
    private const string AnimeSectionOrderKey = "anime_section_order";
    private const string MangaSectionOrderKey = "manga_section_order";

    /// <summary>
    /// The preferences store these methods read and write through (#121).
    /// <para>
    /// Every path that ends here — <see cref="Load"/>, <see cref="Save"/>, <see cref="Clear"/>,
    /// <see cref="SyncFromViewer"/>, and so anything reaching them, such as a successful
    /// <c>SettingsPageModel.LoadAsync</c> — used to be untestable: the static
    /// <c>Preferences.Default</c> throws <c>NotImplementedInReferenceAssemblyException</c> on the
    /// plain <c>net10.0</c> TFM the tests build against. Tests assign a dictionary-backed fake here
    /// instead.
    /// </para>
    /// <para>
    /// Safe as a field initializer, which is why there is no lazy-init dance and no startup wiring:
    /// the <c>Preferences.Default</c> getter itself returns an implementation off-device and only
    /// <c>Get</c>/<c>Set</c>/<c>Remove</c> throw (verified against Microsoft.Maui.Essentials
    /// 10.0.60). Production behaviour is unchanged because nothing ever reassigns it.
    /// </para>
    /// <para>
    /// A mutable static is a service locator, which is not lovely. It is bounded deliberately:
    /// <c>AppSettings</c> is already process-wide static because <c>Media.DisplayTitle</c> and
    /// <c>MediaListEntry.ScoreDisplay</c> consult it and DI never constructs those POCOs, so this
    /// does not make the design worse. Full <c>IAppSettings</c> injection stays available later
    /// (#52). Cross-test pollution is handled by <c>AppSettingsCollection</c>, which serialises
    /// every test class that writes to these statics.
    /// </para>
    /// </summary>
    internal static IPreferences Storage { get; set; } = Preferences.Default;

    /// <summary>
    /// True between a local change to the matching setting and the AniList save that confirms it.
    /// While set, <see cref="SyncFromViewer"/> leaves that setting alone.
    /// <para>
    /// Without this, a Library refresh landing inside the Settings debounce window would read the
    /// server's not-yet-updated copy and silently revert the choice the user had just made — for the
    /// adult toggle, re-enabling 18+ content app-wide. Flushing the debounce on navigate-away (see
    /// <c>SettingsPageModel.FlushPendingSaveAsync</c>) narrows that window but cannot close it: MAUI
    /// Shell does not guarantee the outgoing page's OnDisappearing runs before the incoming page's
    /// OnAppearing, and a save can fail outright, leaving the server genuinely stale. The invariant
    /// this encodes — a local change outranks the server's copy until the server confirms it — holds
    /// in both cases.
    /// </para>
    /// <para>
    /// Scoped to the adult toggle when it arrived in <c>c4a2830</c>, and widened to the other two
    /// user-editable settings in #128: a failed title-language or score-format save was reverted the
    /// next time Settings opened, with nothing said. <see cref="AnimeSectionOrder"/> is deliberately
    /// left out — it is not editable in this app, so it can only ever come from the server.
    /// </para>
    /// <para>
    /// Only a setting with a pending local write is shadowed, and only until
    /// <see cref="SyncFromViewer"/> sees the server agree, or <see cref="Clear"/> runs on sign-out.
    /// Cross-device changes to everything else keep applying on every sync.
    /// </para>
    /// </summary>
    private static bool _displayAdultContentAwaitingUpstream;
    private static bool _titleLanguageAwaitingUpstream;
    private static bool _scoreFormatAwaitingUpstream;

    public static void Load()
    {
        var titleLang = Storage.Get(TitleLanguageKey, nameof(UserTitleLanguage.Romaji));
        if (Enum.TryParse<UserTitleLanguage>(titleLang, out var parsedLang))
        {
            TitleLanguage = parsedLang;
        }

        var scoreFmt = Storage.Get(ScoreFormatKey, nameof(ScoreFormat.Point100));
        if (Enum.TryParse<ScoreFormat>(scoreFmt, out var parsedFmt))
        {
            ScoreFormat = parsedFmt;
        }

        DisplayAdultContent = Storage.Get(DisplayAdultContentKey, false);

        AnimeSectionOrder = ReadSectionOrder(AnimeSectionOrderKey);
        MangaSectionOrder = ReadSectionOrder(MangaSectionOrderKey);
    }

    /// <summary>Section orders persist as a CSV of the server’s own list names.</summary>
    private static List<string> ReadSectionOrder(string key)
    {
        var csv = Storage.Get(key, string.Empty);
        return string.IsNullOrEmpty(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static void Save()
    {
        Storage.Set(TitleLanguageKey, TitleLanguage.ToString());
        Storage.Set(ScoreFormatKey, ScoreFormat.ToString());
        Storage.Set(DisplayAdultContentKey, DisplayAdultContent);
        Storage.Set(AnimeSectionOrderKey, string.Join(",", AnimeSectionOrder));
        Storage.Set(MangaSectionOrderKey, string.Join(",", MangaSectionOrder));
    }

    /// <summary>
    /// Commits an adult-content change and persists just that key (#118).
    /// <para>
    /// Called the moment the Settings toggle flips, ahead of the 1500 ms debounce that saves the
    /// profile to AniList. Before this, the value reached here only when the server response was
    /// applied, so a user who turned 18+ content off and tabbed straight to a browse surface hit
    /// its OnAppearing check while the old value was still live — nothing looked stale, so nothing
    /// invalidated, and the 18+ results stayed on screen until a whole tab cycle later.
    /// </para>
    /// <para>
    /// Writes one key rather than calling <see cref="Save"/>, which would flush all four from
    /// statics that no viewer sync has confirmed yet. If the AniList save later fails, this value
    /// stands — it matches the toggle the user is looking at, and for this setting the user's
    /// intent is the safer of the two to honour.
    /// </para>
    /// </summary>
    public static void SetDisplayAdultContent(bool value)
    {
        DisplayAdultContent = value;
        _displayAdultContentAwaitingUpstream = true;
        Storage.Set(DisplayAdultContentKey, value);
    }

    /// <summary>
    /// Commits a title-language change and persists just that key (#128), the same shape as
    /// <see cref="SetDisplayAdultContent"/>.
    /// <para>
    /// Before this the value reached here only via <see cref="SyncFromViewer"/>, i.e. once a save had
    /// succeeded — so the choice took 1.5 s to appear anywhere else in the app, and never appeared at
    /// all if the save failed.
    /// </para>
    /// </summary>
    public static void SetTitleLanguage(UserTitleLanguage value)
    {
        TitleLanguage = value;
        _titleLanguageAwaitingUpstream = true;
        Storage.Set(TitleLanguageKey, value.ToString());
    }

    /// <summary>Commits a score-format change and persists just that key. See <see cref="SetTitleLanguage"/>.</summary>
    public static void SetScoreFormat(ScoreFormat value)
    {
        ScoreFormat = value;
        _scoreFormatAwaitingUpstream = true;
        Storage.Set(ScoreFormatKey, value.ToString());
    }

    /// <summary>
    /// The value a caller should show for DisplayAdultContent given what the server just reported:
    /// the server's, unless a local change is still awaiting confirmation and the server disagrees
    /// with it — in which case the local choice wins and stays pending.
    /// <para>
    /// Exists so the Settings toggle and this static can never disagree. <c>PopulateFromUser</c>
    /// assigns the bound property before <see cref="SyncFromViewer"/> runs, and that assignment
    /// writes through to <see cref="SetDisplayAdultContent"/>, so without resolving first a stale
    /// viewer would overwrite the pending value before the guard below was ever consulted.
    /// </para>
    /// </summary>
    public static bool ResolveDisplayAdultContent(bool serverValue)
        => _displayAdultContentAwaitingUpstream && serverValue != DisplayAdultContent
            ? DisplayAdultContent
            : serverValue;

    /// <summary>The value the Settings control should show for title language. See <see cref="ResolveDisplayAdultContent"/>.</summary>
    public static UserTitleLanguage ResolveTitleLanguage(UserTitleLanguage serverValue)
        => _titleLanguageAwaitingUpstream && serverValue != TitleLanguage ? TitleLanguage : serverValue;

    /// <summary>The value the Settings control should show for score format. See <see cref="ResolveDisplayAdultContent"/>.</summary>
    public static ScoreFormat ResolveScoreFormat(ScoreFormat serverValue)
        => _scoreFormatAwaitingUpstream && serverValue != ScoreFormat ? ScoreFormat : serverValue;

    /// <summary>
    /// Clears the pending markers for the values an <c>UpdateUser</c> that succeeded actually carried
    /// (#128).
    /// <para>
    /// The comparison in <see cref="SyncFromViewer"/> already clears a marker the response agrees
    /// with, which covers the normal case. This covers the one it cannot: a server that accepted the
    /// request but reported something else back. Leaving that pending would hold a value AniList has
    /// declined and re-send it on every navigate-away, forever.
    /// </para>
    /// <para>
    /// Each check is against what was <em>sent</em>, not blanket. A save is only a ruling on the
    /// values it carried, and the user can change a setting while one is in flight — clearing that
    /// setting's marker too would discard a change the server has never seen.
    /// </para>
    /// </summary>
    public static void ConfirmSettingsSaved(
        UserTitleLanguage sentTitleLanguage,
        ScoreFormat sentScoreFormat,
        bool sentDisplayAdultContent)
    {
        if (sentTitleLanguage == TitleLanguage)
        {
            _titleLanguageAwaitingUpstream = false;
        }

        if (sentScoreFormat == ScoreFormat)
        {
            _scoreFormatAwaitingUpstream = false;
        }

        if (sentDisplayAdultContent == DisplayAdultContent)
        {
            _displayAdultContentAwaitingUpstream = false;
        }
    }

    /// <summary>
    /// Syncs local app settings from an AniList Viewer response.
    /// Called on every Library load/refresh and when the Settings page loads.
    /// </summary>
    public static void SyncFromViewer(AniListUser user)
    {
        // The server's value wins unless a local change is still unconfirmed and the server
        // disagrees with it — see the markers' remarks, and PendingValue.Resolve for the rule.
        //
        // Section order is the exception and follows the server unconditionally: there is no control
        // for it in this app, so a local value could only ever have come from the server anyway.
        TitleLanguage = PendingValue.Resolve(ref _titleLanguageAwaitingUpstream, user.Options.TitleLanguage, TitleLanguage);
        ScoreFormat = PendingValue.Resolve(ref _scoreFormatAwaitingUpstream, user.ScoreFormat, ScoreFormat);
        DisplayAdultContent = PendingValue.Resolve(ref _displayAdultContentAwaitingUpstream, user.Options.DisplayAdultContent, DisplayAdultContent);
        AnimeSectionOrder = user.AnimeSectionOrder;
        MangaSectionOrder = user.MangaSectionOrder;

        Save();
    }

    public static void Clear()
    {
        TitleLanguage = UserTitleLanguage.Romaji;
        ScoreFormat = ScoreFormat.Point100;
        DisplayAdultContent = false;

        // Sign-out must not leave the previous account's unconfirmed change shadowing the next
        // viewer's real preference.
        _displayAdultContentAwaitingUpstream = false;
        _titleLanguageAwaitingUpstream = false;
        _scoreFormatAwaitingUpstream = false;
        Storage.Remove(TitleLanguageKey);
        Storage.Remove(ScoreFormatKey);
        Storage.Remove(DisplayAdultContentKey);
        AnimeSectionOrder = [];
        MangaSectionOrder = [];
        Storage.Remove(AnimeSectionOrderKey);
        Storage.Remove(MangaSectionOrderKey);
    }
}
