namespace AniSprinkles.Models;

public class VoiceActor
{
    public int Id { get; set; }
    public CharacterName? Name { get; set; }
    public CharacterImage? Image { get; set; }
    public string? Language { get; set; }
}
