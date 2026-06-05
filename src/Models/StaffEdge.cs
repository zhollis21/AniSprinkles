using AniSprinkles.Utilities;

namespace AniSprinkles.Models;

public class StaffEdge
{
    public StaffNode? Node { get; set; }
    public string? Role { get; set; }

    // The card's metric badge (favourites), stamped by the PageModel; null when the value is absent.
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;
}

public class StaffNode
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
    public int? Favourites { get; set; }

    public bool HasFavourites => Favourites is > 0;
    public string FavouritesDisplay => MetricFormat.Compact(Favourites);
}
