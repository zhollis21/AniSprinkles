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
    public UserAnimeStatistics AnimeStatistics { get; set; } = new();
}
