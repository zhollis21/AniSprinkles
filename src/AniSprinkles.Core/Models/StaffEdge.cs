using AniSprinkles.Utilities;

namespace AniSprinkles.Models;

public class StaffEdge
{
    public StaffNode? Node { get; set; }
    public string? Role { get; set; }

    // The card's metric badge (favourites), stamped by the PageModel; always shown (0 when none).
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;
}

public class StaffNode
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
    public int? Favourites { get; set; }

    /// <summary>The staff carousel card's name, under the viewer's Staff Name Language (#130).</summary>
    public string DisplayName => StaffNameFormat.Display(Name);

    public bool HasFavourites => Favourites is > 0;
    public string FavouritesDisplay => MetricFormat.Compact(Favourites);
}
