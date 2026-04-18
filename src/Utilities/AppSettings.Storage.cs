using AniSprinkles.Models;

namespace AniSprinkles.Utilities;

public static partial class AppSettings
{
    private const string TitleLanguageKey = "title_language";
    private const string ScoreFormatKey = "score_format";
    private const string DisplayAdultContentKey = "display_adult_content";
    private const string AnimeSectionOrderKey = "anime_section_order";

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

        var sectionOrderCsv = Preferences.Default.Get(AnimeSectionOrderKey, string.Empty);
        AnimeSectionOrder = string.IsNullOrEmpty(sectionOrderCsv)
            ? []
            : sectionOrderCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static void Save()
    {
        Preferences.Default.Set(TitleLanguageKey, TitleLanguage.ToString());
        Preferences.Default.Set(ScoreFormatKey, ScoreFormat.ToString());
        Preferences.Default.Set(DisplayAdultContentKey, DisplayAdultContent);
        Preferences.Default.Set(AnimeSectionOrderKey, string.Join(",", AnimeSectionOrder));
    }

    /// <summary>
    /// Syncs local app settings from an AniList Viewer response.
    /// Called on every My Anime load/refresh and when the Settings page loads.
    /// </summary>
    public static void SyncFromViewer(AniListUser user)
    {
        TitleLanguage = user.Options.TitleLanguage;
        ScoreFormat = user.ScoreFormat;
        DisplayAdultContent = user.Options.DisplayAdultContent;
        AnimeSectionOrder = user.AnimeSectionOrder;
        Save();
    }

    public static void Clear()
    {
        TitleLanguage = UserTitleLanguage.Romaji;
        ScoreFormat = ScoreFormat.Point100;
        DisplayAdultContent = false;
        Preferences.Default.Remove(TitleLanguageKey);
        Preferences.Default.Remove(ScoreFormatKey);
        Preferences.Default.Remove(DisplayAdultContentKey);
        AnimeSectionOrder = [];
        Preferences.Default.Remove(AnimeSectionOrderKey);
    }
}
