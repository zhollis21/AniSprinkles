namespace AniSprinkles.Models;

public class StaffEdge
{
    public StaffNode? Node { get; set; }
    public string? Role { get; set; }
}

public class StaffNode
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
}
