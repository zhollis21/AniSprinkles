using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public partial class MediaListEntry : ObservableObject
{
    public int Id { get; set; }
    public int MediaId { get; set; }
    public Media? Media { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(CanIncrementProgress))]
    [NotifyPropertyChangedFor(nameof(MetadataDisplay))]
    private MediaListStatus? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(CanIncrementProgress))]
    [NotifyPropertyChangedFor(nameof(MetadataDisplay))]
    [NotifyPropertyChangedFor(nameof(EpisodesBehind))]
    [NotifyPropertyChangedFor(nameof(AiringInfoDisplay))]
    [NotifyPropertyChangedFor(nameof(HasAiringInfo))]
    private int? _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreDisplay))]
    [NotifyPropertyChangedFor(nameof(HasScore))]
    [NotifyPropertyChangedFor(nameof(MetadataDisplay))]
    private double? _score;

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

    /// <summary>True when the user has assigned a non-zero score.</summary>
    public bool HasScore => Score is not null and not 0;

    public string ScoreDisplay => Score is null or 0 ? string.Empty : AppSettings.ScoreFormat switch
    {
        ScoreFormat.Point100 => Score.Value.ToString("0"),
        ScoreFormat.Point10Decimal => Score.Value.ToString("0.0"),
        ScoreFormat.Point10 => Score.Value.ToString("0"),
        ScoreFormat.Point5 => new string('\u2605', (int)Score.Value) + new string('\u2606', 5 - (int)Score.Value),
        ScoreFormat.Point3 => Score.Value switch { >= 3 => "\U0001F60A", >= 2 => "\U0001F610", _ => "\U0001F61E" },
        _ => Score.Value.ToString("0.0"),
    };

    /// <summary>True when the score format uses numeric values (not stars or smileys).</summary>
    public static bool IsNumericScoreFormat => AppSettings.ScoreFormat is ScoreFormat.Point100
        or ScoreFormat.Point10Decimal or ScoreFormat.Point10;

    /// <summary>
    /// Maximum episode count for +1 cap logic.
    /// Uses the next airing episode number (latest aired = episode - 1) if available,
    /// otherwise falls back to the total episode count.
    /// </summary>
    public int? MaxEpisodes => Media?.Episodes;

    /// <summary>
    /// Number of episodes behind for currently airing shows.
    /// Null if not applicable (not airing, or progress is current).
    /// </summary>
    public int? EpisodesBehind
    {
        get
        {
            if (Media?.NextAiringEpisode?.Episode is not int nextEp)
            {
                return null;
            }

            var watched = Progress ?? 0;
            var aired = nextEp - 1; // next ep hasn't aired yet
            var behind = aired - watched;
            return behind > 0 ? behind : null;
        }
    }

    /// <summary>
    /// Display string for airing info, e.g. "2 ep behind · Ep 8 in 3d".
    /// Null if the show is not currently airing.
    /// </summary>
    public string? AiringInfoDisplay
    {
        get
        {
            if (Media?.NextAiringEpisode is null)
            {
                return null;
            }

            var parts = new List<string>();
            var behind = EpisodesBehind;

            if (behind > 0)
            {
                parts.Add(behind == 1 ? "1 ep behind" : $"{behind} eps behind");
            }

            if (Media.NextAiringEpisode.AiringAt is int airingAt)
            {
                var airingDate = DateTimeOffset.FromUnixTimeSeconds(airingAt);
                var timeUntil = airingDate - DateTimeOffset.UtcNow;

                if (timeUntil.TotalSeconds > 0)
                {
                    if (timeUntil.TotalHours < 1)
                    {
                        parts.Add($"Ep {Media.NextAiringEpisode.Episode} in {timeUntil.Minutes}m");
                    }
                    else if (timeUntil.TotalDays < 1)
                    {
                        parts.Add($"Ep {Media.NextAiringEpisode.Episode} in {(int)timeUntil.TotalHours}h");
                    }
                    else if (timeUntil.TotalDays < 7)
                    {
                        parts.Add($"Ep {Media.NextAiringEpisode.Episode} in {(int)timeUntil.TotalDays}d");
                    }
                    else
                    {
                        parts.Add($"Ep {Media.NextAiringEpisode.Episode} {airingDate.LocalDateTime:MMM d}");
                    }
                }
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
    }

    public bool HasAiringInfo => AiringInfoDisplay is not null;

    /// <summary>
    /// Combined single-line metadata: "3/12 · 8 · 2 eps behind · Ep 8 in 3d".
    /// Used by the standard list template to show all info on one line.
    /// </summary>
    public string MetadataDisplay
    {
        get
        {
            var parts = new List<string> { ProgressDisplay };

            if (HasScore)
            {
                parts.Add(ScoreDisplay);
            }

            var airing = AiringInfoDisplay;
            if (airing is not null)
            {
                parts.Add(airing);
            }

            return string.Join(" \u00b7 ", parts);
        }
    }

    /// <summary>
    /// True when the +1 increment button should be shown.
    /// Visible for Watching/Rewatching entries that haven't yet reached the max episode count.
    /// </summary>
    public bool CanIncrementProgress =>
        Status is MediaListStatus.Current or MediaListStatus.Repeating
        && (MaxEpisodes is null || Progress < MaxEpisodes);
}
