using AniSprinkles.Utilities;

namespace AniSprinkles.Models;

public class VoiceActor
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
    public string? Language { get; set; }
    public int? Favourites { get; set; }

    /// <summary>The voice-actor card's name, under the viewer's Staff Name Language (#130).</summary>
    public string DisplayName => StaffNameFormat.Display(Name);

    public bool HasFavourites => Favourites is > 0;
    public string FavouritesDisplay => MetricFormat.Compact(Favourites);

    // Always-on heart on the card shows "0" rather than blank when a VA has no favourites.
    public string FavouritesOrZero => MetricFormat.CompactOrZero(Favourites);
}
