namespace AniSprinkles.Models;

public class MediaListEntry
{
    public int Id { get; set; }
    public int MediaId { get; set; }
    public Media? Media { get; set; }
    public MediaListStatus? Status { get; set; }
    public int? Progress { get; set; }
    public double? Score { get; set; }
    public int? Repeat { get; set; }
    public string? Notes { get; set; }
    public bool? Private { get; set; }
    public bool? HiddenFromStatusLists { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public string StatusDisplay => Status?.ToString() ?? "Unknown";

    public string ProgressDisplay
    {
        get
        {
            if (Progress is null)
            {
                return "-";
            }

            var total = Media?.Episodes;
            return total is null ? $"{Progress}" : $"{Progress}/{total}";
        }
    }

    public string ScoreDisplay
        => Score is null ? "-" : Score.Value.ToString("0.0");
}
