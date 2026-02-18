namespace AniSprinkles.Models;

public class Character
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
}

public class CharacterName
{
    public string? Full { get; set; }
    public string? Native { get; set; }
}

public class CharacterImage
{
    public string? Medium { get; set; }
    public string? Large { get; set; }
}
