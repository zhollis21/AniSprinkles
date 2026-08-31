namespace AniSprinkles.Models;

public class AniListUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? About { get; set; }
    public string? AvatarLarge { get; set; }
    public string? AvatarMedium { get; set; }
    public string? BannerImage { get; set; }
    public string? SiteUrl { get; set; }
    public int? DonatorTier { get; set; }
    public string? DonatorBadge { get; set; }
    public UserOptions Options { get; set; } = new();
    public ScoreFormat ScoreFormat { get; set; }
    public string? RowOrder { get; set; }
    public List<string> AnimeSectionOrder { get; set; } = [];

    /// <summary>
    /// The manga list’s own section order (#12). Separate from the anime one because the names
    /// differ — AniList returns Reading/Rereading where the anime list says Watching/Rewatching —
    /// so ordering the manga tab by the anime list would sort against names it never contains.
    /// </summary>
    public List<string> MangaSectionOrder { get; set; } = [];
    public UserAnimeStatistics AnimeStatistics { get; set; } = new();
    public UserMangaStatistics MangaStatistics { get; set; } = new();
}
