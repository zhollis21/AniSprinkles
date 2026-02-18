using AniSprinkles.Utilities;

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

    public string ScoreDisplay => Score is null ? "-" : AppSettings.ScoreFormat switch
    {
        ScoreFormat.Point100 => Score.Value.ToString("0"),
        ScoreFormat.Point10Decimal => Score.Value.ToString("0.0"),
        ScoreFormat.Point10 => Score.Value.ToString("0"),
        ScoreFormat.Point5 => new string('\u2605', (int)Score.Value) + new string('\u2606', 5 - (int)Score.Value),
        ScoreFormat.Point3 => Score.Value switch { >= 3 => "\U0001F60A", >= 2 => "\U0001F610", _ => "\U0001F61E" },
        _ => Score.Value.ToString("0.0"),
    };
}
