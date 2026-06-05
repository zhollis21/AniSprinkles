namespace AniSprinkles.Models;

public class CharacterEdge
{
    public Character? Node { get; set; }
    public string? Role { get; set; }
    public List<VoiceActor> VoiceActors { get; set; } = [];
    public bool HasVoiceActor => VoiceActors.Count > 0;

    // The card's metric badge (favourites), stamped by the PageModel; null when the value is absent.
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;
}
