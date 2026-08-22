namespace AniSprinkles.Models;

public class StaffCharacterEdge
{
    public Character? Node { get; set; }
    public string? Role { get; set; }
    public RelatedMedia? Media { get; set; }
}
