namespace AniSprinkles.Models;

public class CharacterMediaEdge
{
    public RelatedMedia? Node { get; set; }
    public string? CharacterRole { get; set; }
    public List<VoiceActor> VoiceActors { get; set; } = [];
}
