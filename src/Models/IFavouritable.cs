namespace AniSprinkles.Models;

/// <summary>
/// A details-page entity (media, character, staff, studio) the viewer can favorite. Lets
/// <see cref="AniSprinkles.PageModels.FavouriteToggleRunner"/> apply the optimistic flip + count bump
/// uniformly across all four types.
/// </summary>
public interface IFavouritable
{
    int Id { get; }

    /// <summary>The viewer's favorite state (from <c>isFavourite</c>).</summary>
    bool IsFavourite { get; set; }

    /// <summary>Global favourite count; bumped ±1 optimistically alongside <see cref="IsFavourite"/>.</summary>
    int? Favourites { get; set; }
}
