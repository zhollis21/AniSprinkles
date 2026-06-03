using System;
using System.Globalization;
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
    public int? Favourites { get; set; }
    public int? Popularity { get; set; }
    public MediaDate? StartDate { get; set; }

    public string DisplayTitle => AppSettings.TitleLanguage switch
    {
        UserTitleLanguage.English => Title?.English ?? Title?.Romaji ?? Title?.Native ?? "Unknown",
        UserTitleLanguage.Native => Title?.Native ?? Title?.Romaji ?? Title?.English ?? "Unknown",
        _ => Title?.Romaji ?? Title?.English ?? Title?.Native ?? "Unknown",
    };

    /// <summary>
    /// True when this entry is an anime. The media detail screen queries
    /// <c>Media(id:, type: ANIME)</c>, so navigating to a non-anime id (manga/novel) returns 404.
    /// Tile navigation gates on this to show a "not supported" toast instead of a doomed fetch.
    /// </summary>
    public bool IsAnime => string.Equals(Type, "ANIME", StringComparison.OrdinalIgnoreCase);

    public bool HasScore => AverageScore is > 0;
    public bool HasFavourites => Favourites is > 0;
    public bool HasPopularity => Popularity is > 0;
    public bool HasYear => StartDate?.Year is > 0;

    // AniList stores scores 0–100; render as a 0–10.0 rating since that scale is more recognizable.
    public string ScoreDisplay => HasScore
        ? (AverageScore!.Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture)
        : string.Empty;

    public string FavouritesDisplay => Favourites switch
    {
        null or <= 0 => string.Empty,
        >= 1000 => (Favourites.Value / 1000.0).ToString("0.#k", CultureInfo.InvariantCulture),
        _ => Favourites.Value.ToString(CultureInfo.InvariantCulture),
    };

    public string PopularityDisplay => Popularity switch
    {
        null or <= 0 => string.Empty,
        >= 1000 => (Popularity.Value / 1000.0).ToString("0.#k", CultureInfo.InvariantCulture),
        _ => Popularity.Value.ToString(CultureInfo.InvariantCulture),
    };

    public string YearDisplay => HasYear ? StartDate!.Year!.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
}
