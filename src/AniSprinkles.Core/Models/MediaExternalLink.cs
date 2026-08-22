namespace AniSprinkles.Models;

public class MediaExternalLink
{
    public int? Id { get; set; }
    public string? Url { get; set; }
    public string? Site { get; set; }
    public string? Type { get; set; }
    public string? Language { get; set; }
    public string? Color { get; set; }
    public bool? IsDisabled { get; set; }
}
