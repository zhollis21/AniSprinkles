namespace AniSprinkles.Models;

public class CharacterEdge
{
    public Character? Node { get; set; }
    public string? Role { get; set; }
    public List<VoiceActor> VoiceActors { get; set; } = [];
    public bool HasVoiceActor => VoiceActors.Count > 0;

    // The card's metric badge (favourites), stamped by the PageModel; always shown (0 when none).
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;
}
