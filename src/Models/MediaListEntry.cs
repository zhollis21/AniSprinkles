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
    [NotifyPropertyChangedFor(nameof(ShouldShowIncrementButton))]
    [NotifyPropertyChangedFor(nameof(MetadataDisplay))]
    private MediaListStatus? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(CanIncrementProgress))]
    [NotifyPropertyChangedFor(nameof(MetadataDisplay))]
    [NotifyPropertyChangedFor(nameof(EpisodesBehind))]
    [NotifyPropertyChangedFor(nameof(EpisodesBehindDisplay))]
    [NotifyPropertyChangedFor(nameof(HasEpisodesBehindDisplay))]
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

            // Use MaxEpisodes (which falls back to NextAiringEpisode.Episode - 1 for
            // long-running airing shows) so the list display matches the Details page.
            var total = MaxEpisodes;
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
    public bool IsNumericScoreFormat => AppSettings.ScoreFormat is ScoreFormat.Point100
        or ScoreFormat.Point10Decimal or ScoreFormat.Point10;

    /// <summary>
    /// Cap for +1 / progress-slider logic. Uses the total episode count when known,
    /// otherwise falls back to the most-recently-aired episode (<c>NextAiringEpisode.Episode - 1</c>)
    /// so users of currently-airing shows stop at the latest episode they could have watched.
    /// Null when neither is known.
    /// </summary>
    public int? MaxEpisodes =>
        Media?.Episodes is > 0 ? Media.Episodes :
        Media?.NextAiringEpisode?.Episode is > 1 ? Media.NextAiringEpisode.Episode - 1 :
        null;

    /// <summary>
    /// True only when the total episode count is known (i.e. the show has a finite,
    /// declared length). Used to gate the completion flow — long-running airing shows
    /// without a known total should not trigger completion when the cap is reached.
    /// </summary>
    public bool HasKnownEpisodeCount => Media?.Episodes is > 0;

    /// <summary>
    /// Whether the +1 control should be *rendered* at all. True for Watching/Rewatching
    /// statuses regardless of whether the user has caught up to the cap; the control is
    /// still hidden entirely for other statuses. Caught-up state is expressed visually
    /// via <see cref="CanIncrementProgress"/> (dimmed) rather than by disappearing.
    /// </summary>
    public bool ShouldShowIncrementButton =>
        Status is MediaListStatus.Current or MediaListStatus.Repeating;

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
    /// Standalone "X eps behind" display string. Null when there's nothing to show
    /// (not airing, or progress is current).
    /// </summary>
    public string? EpisodesBehindDisplay
    {
        get
        {
            var behind = EpisodesBehind;
            if (behind is not > 0)
            {
                return null;
            }

            return behind == 1 ? "1 ep behind" : $"{behind} eps behind";
        }
    }

    public bool HasEpisodesBehindDisplay => EpisodesBehindDisplay is not null;

    /// <summary>
    /// Standalone "Ep N in Xd" countdown for the next airing episode. Null when not
    /// airing or the airing time is unknown / in the past.
    /// </summary>
    public string? NextEpisodeDisplay
    {
        get
        {
            if (Media?.NextAiringEpisode is not { Episode: var episode, AiringAt: int airingAt })
            {
                return null;
            }

            var airingDate = DateTimeOffset.FromUnixTimeSeconds(airingAt);
            var timeUntil = airingDate - DateTimeOffset.UtcNow;

            if (timeUntil.TotalSeconds <= 0)
            {
                return null;
            }

            if (timeUntil.TotalHours < 1)
            {
                return $"Ep {episode} in {timeUntil.Minutes}m";
            }
            if (timeUntil.TotalDays < 1)
            {
                return $"Ep {episode} in {(int)timeUntil.TotalHours}h";
            }
            if (timeUntil.TotalDays < 7)
            {
                return $"Ep {episode} in {(int)timeUntil.TotalDays}d";
            }
            return $"Ep {episode} {airingDate.LocalDateTime:MMM d}";
        }
    }

    public bool HasNextEpisodeDisplay => NextEpisodeDisplay is not null;

    /// <summary>
    /// Single-line airing summary, e.g. "2 eps behind · Ep 8 in 3d". Used by the
    /// Standard template (which shows everything on one row); the Large template
    /// uses the two split properties instead so each line is short and tidy.
    /// </summary>
    public string? AiringInfoDisplay
    {
        get
        {
            var parts = new List<string>();
            if (EpisodesBehindDisplay is { } behindStr)
            {
                parts.Add(behindStr);
            }
            if (NextEpisodeDisplay is { } nextStr)
            {
                parts.Add(nextStr);
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
    /// True when an additional episode is currently watchable — the entry is in
    /// Watching/Rewatching status AND progress hasn't reached the cap. Used to express
    /// the dimmed/caught-up state of the +1 control; it does <b>not</b> control
    /// visibility (see <see cref="ShouldShowIncrementButton"/> for that).
    /// </summary>
    public bool CanIncrementProgress =>
        Status is MediaListStatus.Current or MediaListStatus.Repeating
        && (MaxEpisodes is null || Progress < MaxEpisodes);
}
