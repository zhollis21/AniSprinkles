using AniSprinkles.Utilities;

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

    /// <summary>
    /// Whether to show the "Main Studio" label on the Media Details studios row. Set by
    /// <c>BuildStudioChips</c> only for the main studio when there's more than one — with a single
    /// studio the "main" distinction is implied, so it stays off.
    /// </summary>
    public bool ShowMainStudioLabel { get; set; }

    /// <summary>Compact favourites for list cards (k-format, blank when missing) — mirrors the other detail models.</summary>
    public string FavouritesDisplay => MetricFormat.Compact(Favourites);

    public bool HasFavourites => Favourites is > 0;
}
