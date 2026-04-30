namespace AniSprinkles.Models;

public class Staff
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
    public string? SiteUrl { get; set; }

    public List<StaffCharacterEdge> Characters { get; set; } = [];
    public PageInfo? CharactersPageInfo { get; set; }

    public List<StaffMediaEdge> StaffMedia { get; set; } = [];
    public PageInfo? StaffMediaPageInfo { get; set; }

    public string DisplayName => Name?.Full ?? Name?.UserPreferred ?? Name?.Native ?? "Unknown";
}
