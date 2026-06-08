namespace AniSprinkles.Models;

public class Studio
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool? IsAnimationStudio { get; set; }
    public bool? IsMain { get; set; }
    public int? Favourites { get; set; }
    public string? SiteUrl { get; set; }
    public List<StudioMediaEdge> Media { get; set; } = [];
    public PageInfo? MediaPageInfo { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Studio" : Name;
}
