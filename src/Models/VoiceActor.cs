using System.Globalization;

namespace AniSprinkles.Models;

public class VoiceActor
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
    public string? Language { get; set; }
    public int? Favourites { get; set; }

    public bool HasFavourites => Favourites is > 0;

    public string FavouritesDisplay => Favourites switch
    {
        null or <= 0 => string.Empty,
        >= 1000 => (Favourites.Value / 1000.0).ToString("0.#k", CultureInfo.InvariantCulture),
        _ => Favourites.Value.ToString(CultureInfo.InvariantCulture),
    };
}
