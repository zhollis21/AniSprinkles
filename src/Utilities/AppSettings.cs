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

    public static UserTitleLanguage TitleLanguage { get; set; } = UserTitleLanguage.Romaji;
    public static ScoreFormat ScoreFormat { get; set; } = ScoreFormat.Point100;
    public static bool DisplayAdultContent { get; set; }

    public static void Load()
    {
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
    }

    public static void Save()
    {
        Preferences.Default.Set(TitleLanguageKey, TitleLanguage.ToString());
        Preferences.Default.Set(ScoreFormatKey, ScoreFormat.ToString());
        Preferences.Default.Set(DisplayAdultContentKey, DisplayAdultContent);
    }
}
