namespace AniSprinkles.Models;

public class CharacterEdge
{
    public Character? Node { get; set; }
    public string? Role { get; set; }
    public List<VoiceActor> VoiceActors { get; set; } = [];
    public bool HasVoiceActor => VoiceActors.Count > 0;
}
