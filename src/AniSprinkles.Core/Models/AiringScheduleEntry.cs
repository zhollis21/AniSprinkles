namespace AniSprinkles.Models;

public class AiringScheduleEntry
{
    public int Id { get; set; }

    /// <summary>Unix timestamp (UTC seconds) when the episode airs.</summary>
    public int AiringAt { get; set; }

    public int Episode { get; set; }
    public int MediaId { get; set; }

    /// <summary>The user-preferred title for the media.</summary>
    public string MediaTitle { get; set; } = string.Empty;

    /// <summary>Cover image URL (medium size) for rich notifications.</summary>
    public string? CoverImageUrl { get; set; }
}
