namespace AniSprinkles.Utilities;

/// <summary>
/// Static accessor for user display preferences that need to be consulted
/// by model-level computed properties (DisplayTitle, ScoreDisplay, etc.).
/// Values are persisted to Preferences for offline startup and updated
/// from the AniList Viewer response when the user signs in.
/// </summary>
public static class AppSettings
{
    private const string TitleLanguageKey = "title_language";
    private const string ScoreFormatKey = "score_format";
    private const string DisplayAdultContentKey = "display_adult_content";
    private const string HasSyncedKey = "has_synced_prefs";
    private const string AnimeSectionOrderKey = "anime_section_order";

    public static UserTitleLanguage TitleLanguage { get; set; } = UserTitleLanguage.Romaji;
    public static ScoreFormat ScoreFormat { get; set; } = ScoreFormat.Point100;
    public static bool DisplayAdultContent { get; set; }
    public static List<string> AnimeSectionOrder { get; set; } = [];

    /// <summary>True once preferences have been synced from an AniList Viewer response.</summary>
    public static bool HasSynced { get; private set; }

    /// <summary>
    /// UTC timestamp of the most recent sync attempt, stamped at the start of the attempt
    /// before the network call completes. Used to de-duplicate concurrent viewer requests
    /// within a single session (e.g. the startup sync in App racing with the first LoadAsync).
    /// Reset to default on failure so the next load retries. Not persisted.
    /// </summary>
    public static DateTimeOffset LastSyncAttemptUtc { get; private set; }

    public static void Load()
    {
        HasSynced = Preferences.Default.Get(HasSyncedKey, false);

        var titleLang = Preferences.Default.Get(TitleLanguageKey, nameof(UserTitleLanguage.Romaji));
        if (Enum.TryParse<UserTitleLanguage>(titleLang, out var parsedLang))
        {
            TitleLanguage = parsedLang;
        }

        var scoreFmt = Preferences.Default.Get(ScoreFormatKey, nameof(ScoreFormat.Point100));
        if (Enum.TryParse<ScoreFormat>(scoreFmt, out var parsedFmt))
        {
            ScoreFormat = parsedFmt;
        }

        DisplayAdultContent = Preferences.Default.Get(DisplayAdultContentKey, false);

        var sectionOrderCsv = Preferences.Default.Get(AnimeSectionOrderKey, string.Empty);
        AnimeSectionOrder = string.IsNullOrEmpty(sectionOrderCsv)
            ? []
            : sectionOrderCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static void Save()
    {
        HasSynced = true;
        Preferences.Default.Set(HasSyncedKey, true);
        Preferences.Default.Set(TitleLanguageKey, TitleLanguage.ToString());
        Preferences.Default.Set(ScoreFormatKey, ScoreFormat.ToString());
        Preferences.Default.Set(DisplayAdultContentKey, DisplayAdultContent);
        Preferences.Default.Set(AnimeSectionOrderKey, string.Join(",", AnimeSectionOrder));
    }

    /// <summary>
    /// Stamps <see cref="LastSyncAttemptUtc"/> at the start of a sync attempt, before the
    /// network call completes, so concurrent <c>LoadAsync</c> callers see it immediately
    /// and skip their own viewer fetch. Call <see cref="ClearSyncTimestamp"/> if the attempt
    /// fails so the next load retries normally.
    /// </summary>
    public static void MarkSyncStarted()
    {
        LastSyncAttemptUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Resets <see cref="LastSyncAttemptUtc"/> after a failed attempt so the next load syncs normally.</summary>
    public static void ClearSyncTimestamp()
    {
        LastSyncAttemptUtc = default;
    }

    /// <summary>
    /// Syncs local app settings from an AniList Viewer response.
    /// Called on app startup, on every My Anime load/refresh, and when the Settings page loads.
    /// Updates <see cref="LastSyncAttemptUtc"/> so callers can avoid redundant viewer fetches.
    /// </summary>
    public static void SyncFromViewer(AniListUser user)
    {
        TitleLanguage = user.Options.TitleLanguage;
        ScoreFormat = user.ScoreFormat;
        DisplayAdultContent = user.Options.DisplayAdultContent;
        AnimeSectionOrder = user.AnimeSectionOrder;
        LastSyncAttemptUtc = DateTimeOffset.UtcNow;
        Save();
    }

    public static void Clear()
    {
        TitleLanguage = UserTitleLanguage.Romaji;
        ScoreFormat = ScoreFormat.Point100;
        DisplayAdultContent = false;
        HasSynced = false;
        LastSyncAttemptUtc = default;
        Preferences.Default.Remove(TitleLanguageKey);
        Preferences.Default.Remove(ScoreFormatKey);
        Preferences.Default.Remove(DisplayAdultContentKey);
        AnimeSectionOrder = [];
        Preferences.Default.Remove(HasSyncedKey);
        Preferences.Default.Remove(AnimeSectionOrderKey);
    }
}
