using System.Collections.ObjectModel;
using AniSprinkles.Utilities;

namespace AniSprinkles.Models;

public class Staff : IFavouritable
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
    public string? Description { get; set; }
    public string? LanguageV2 { get; set; }
    public List<string> PrimaryOccupations { get; set; } = [];
    public string? Gender { get; set; }
    public MediaDate? DateOfBirth { get; set; }
    public MediaDate? DateOfDeath { get; set; }
    public int? Age { get; set; }
    public List<int> YearsActive { get; set; } = [];
    public string? HomeTown { get; set; }
    public string? BloodType { get; set; }
    public int? Favourites { get; set; }
    /// <summary>Whether the signed-in viewer has favorited this entity (from <c>isFavourite</c>).</summary>
    public bool IsFavourite { get; set; }
    public string? SiteUrl { get; set; }

    // ObservableCollection so PageModel can append on Load More + clear/replace on sort change
    // and the BindableLayout in XAML refreshes automatically.
    public ObservableCollection<StaffCharacterEdge> Characters { get; } = [];
    public PageInfo? CharactersPageInfo { get; set; }

    public ObservableCollection<StaffMediaEdge> StaffMedia { get; } = [];
    public PageInfo? StaffMediaPageInfo { get; set; }

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
}
