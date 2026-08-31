namespace AniSprinkles.Models;

/// <summary>
/// The viewer's manga totals (#12). Separate from <see cref="UserAnimeStatistics"/> even though
/// AniList returns one <c>UserStatistics</c> type for both: that type carries every field for
/// either kind, so a shared model would put a meaningless <c>EpisodesWatched</c> on manga and a
/// meaningless <c>ChaptersRead</c> on anime. Modelling only the fields that mean something keeps
/// the Settings card from being able to show a number that cannot be true.
/// </summary>
public class UserMangaStatistics
{
    public int Count { get; set; }
    public double MeanScore { get; set; }
    public int ChaptersRead { get; set; }
    public int VolumesRead { get; set; }
}
