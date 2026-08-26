using System.Collections.ObjectModel;
using AniSprinkles.Utilities;

namespace AniSprinkles.Models;

public class Character : IFavouritable
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
    public string? Description { get; set; }
    public string? Gender { get; set; }
    public string? Age { get; set; }
    public string? BloodType { get; set; }
    public MediaDate? DateOfBirth { get; set; }
    public int? Favourites { get; set; }
    /// <summary>Whether the signed-in viewer has favorited this entity (from <c>isFavourite</c>).</summary>
    public bool IsFavourite { get; set; }
    public string? SiteUrl { get; set; }

    // ObservableCollection so PageModel can append on Load More.
    public ObservableCollection<CharacterMediaEdge> Media { get; } = [];
    public PageInfo? MediaPageInfo { get; set; }

    public string DisplayName => StaffNameFormat.Display(Name);

    /// <summary>
    /// Whether the native-script name still earns its place under the hero. Suppressed when the
    /// display name already IS the native name — i.e. the viewer picked Native — so the page does not
    /// print the same name twice (#130).
    /// </summary>
    public bool ShowNativeName
        => !string.IsNullOrWhiteSpace(Name?.Native)
        && !string.Equals(Name!.Native, DisplayName, StringComparison.Ordinal);

    public bool HasFavourites => Favourites is > 0;
    public string FavouritesDisplay => MetricFormat.Compact(Favourites);

    // Always-on heart on a card shows "0" rather than blank when a character has no favourites.
    public string FavouritesOrZero => MetricFormat.CompactOrZero(Favourites);
}

public class CharacterName
{
    public string? Full { get; set; }
    public string? Native { get; set; }
    public string? UserPreferred { get; set; }

    public List<string> Alternative { get; set; } = [];
    public List<string> AlternativeSpoiler { get; set; } = [];
}

public class CharacterImage
{
    public string? Medium { get; set; }
    public string? Large { get; set; }
}
