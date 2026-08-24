namespace AniSprinkles.Utilities;

public static partial class AppSettings
{
    private const string TitleLanguageKey = "title_language";
    private const string ScoreFormatKey = "score_format";
    private const string DisplayAdultContentKey = "display_adult_content";
    private const string AnimeSectionOrderKey = "anime_section_order";

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
    /// True between a local <see cref="SetDisplayAdultContent"/> and the AniList save that confirms
    /// it. While set, <see cref="SyncFromViewer"/> leaves <see cref="DisplayAdultContent"/> alone.
    /// <para>
    /// Without this, a Library refresh landing inside the Settings debounce window would read the
    /// server's not-yet-updated copy and silently revert the toggle the user had just flipped —
    /// re-enabling 18+ content app-wide. Flushing the debounce on navigate-away (see
    /// <c>SettingsPageModel.FlushPendingSaveAsync</c>) narrows that window but cannot close it: MAUI
    /// Shell does not guarantee the outgoing page's OnDisappearing runs before the incoming page's
    /// OnAppearing, and a save can fail outright, leaving the server genuinely stale. The invariant
    /// this encodes — a local change outranks the server's copy until the server confirms it — holds
    /// in both cases.
    /// </para>
    /// <para>
    /// Scoped to this one field deliberately. Cross-device changes to title language, score format
    /// and section order keep applying on every sync; only the setting with a pending local write is
    /// shadowed, and only until <see cref="ConfirmDisplayAdultContentSaved"/> or <see cref="Clear"/>.
    /// </para>
    /// </summary>
    private static bool _displayAdultContentAwaitingUpstream;

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

        var sectionOrderCsv = Storage.Get(AnimeSectionOrderKey, string.Empty);
        AnimeSectionOrder = string.IsNullOrEmpty(sectionOrderCsv)
            ? []
            : sectionOrderCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static void Save()
    {
        Storage.Set(TitleLanguageKey, TitleLanguage.ToString());
        Storage.Set(ScoreFormatKey, ScoreFormat.ToString());
        Storage.Set(DisplayAdultContentKey, DisplayAdultContent);
        Storage.Set(AnimeSectionOrderKey, string.Join(",", AnimeSectionOrder));
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
    /// Marks the local adult-content change as confirmed upstream, so viewer syncs follow the server
    /// again. Called when Settings receives a viewer response — the reply to our own save, or a
    /// fresh load, either of which means the server's copy is no longer behind ours.
    /// </summary>
    public static void ConfirmDisplayAdultContentSaved() => _displayAdultContentAwaitingUpstream = false;

    /// <summary>
    /// Syncs local app settings from an AniList Viewer response.
    /// Called on every My Anime load/refresh and when the Settings page loads.
    /// </summary>
    public static void SyncFromViewer(AniListUser user)
    {
        TitleLanguage = user.Options.TitleLanguage;
        ScoreFormat = user.ScoreFormat;
        AnimeSectionOrder = user.AnimeSectionOrder;

        // Skipped while a local change is still unconfirmed — see the field's remarks. Every other
        // preference above follows the server unconditionally.
        if (!_displayAdultContentAwaitingUpstream)
        {
            DisplayAdultContent = user.Options.DisplayAdultContent;
        }

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
        Storage.Remove(TitleLanguageKey);
        Storage.Remove(ScoreFormatKey);
        Storage.Remove(DisplayAdultContentKey);
        AnimeSectionOrder = [];
        Storage.Remove(AnimeSectionOrderKey);
    }
}
