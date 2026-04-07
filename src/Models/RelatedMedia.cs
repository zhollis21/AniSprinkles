using AniSprinkles.Utilities;

namespace AniSprinkles.Models;

public class RelatedMedia
{
    public int Id { get; set; }
    public MediaTitle? Title { get; set; }
    public string? Format { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public MediaCoverImage? CoverImage { get; set; }
    public int? AverageScore { get; set; }

    public string DisplayTitle => AppSettings.TitleLanguage switch
    {
        UserTitleLanguage.English => Title?.English ?? Title?.Romaji ?? Title?.Native ?? "Unknown",
        UserTitleLanguage.Native => Title?.Native ?? Title?.Romaji ?? Title?.English ?? "Unknown",
        _ => Title?.Romaji ?? Title?.English ?? Title?.Native ?? "Unknown",
    };
}
