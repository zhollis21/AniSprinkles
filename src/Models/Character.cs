using System.Collections.ObjectModel;

namespace AniSprinkles.Models;

public class Character
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
    public string? SiteUrl { get; set; }

    // ObservableCollection so PageModel can append on Load More.
    public ObservableCollection<CharacterMediaEdge> Media { get; } = [];
    public PageInfo? MediaPageInfo { get; set; }

    public string DisplayName => Name?.Full ?? Name?.UserPreferred ?? Name?.Native ?? "Unknown";
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
