namespace AniSprinkles.Utilities;

/// <summary>
/// Static accessor for user display preferences that need to be consulted
/// by model-level computed properties (DisplayTitle, ScoreDisplay, etc.).
/// Persistence and Viewer-sync live in <see cref="AppSettings"/>'s storage partial;
/// the properties here are the public surface that tests and pure code depend on.
/// </summary>
public static partial class AppSettings
{
    public static UserTitleLanguage TitleLanguage { get; set; } = UserTitleLanguage.Romaji;
    public static ScoreFormat ScoreFormat { get; set; } = ScoreFormat.Point100;
    public static bool DisplayAdultContent { get; set; }
    public static List<string> AnimeSectionOrder { get; set; } = [];
}
