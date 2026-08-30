using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public partial class MediaListEntry : ObservableObject, IDisplayProjection
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
    [NotifyPropertyChangedFor(nameof(UsesVolumeProgress))]
    [NotifyPropertyChangedFor(nameof(ActiveProgress))]
    [NotifyPropertyChangedFor(nameof(ActiveProgressUnit))]
    [NotifyPropertyChangedFor(nameof(IncrementLabel))]
    [NotifyPropertyChangedFor(nameof(IncrementDescription))]
    [NotifyPropertyChangedFor(nameof(IncrementHint))]
    [NotifyPropertyChangedFor(nameof(ActiveProgressTotal))]
    [NotifyPropertyChangedFor(nameof(HasKnownProgressTotal))]
    private int? _progress;

    /// <summary>
    /// Volumes read, for manga (AniList's <c>progressVolumes</c>). A counter fully independent of
    /// <see cref="Progress"/> — AniList never derives one from the other — and meaningless for
    /// anime. Which of the two drives the UI is decided by <see cref="UsesVolumeProgress"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(CanIncrementProgress))]
    [NotifyPropertyChangedFor(nameof(MetadataDisplay))]
    [NotifyPropertyChangedFor(nameof(UsesVolumeProgress))]
    [NotifyPropertyChangedFor(nameof(ActiveProgress))]
    [NotifyPropertyChangedFor(nameof(ActiveProgressUnit))]
    [NotifyPropertyChangedFor(nameof(IncrementLabel))]
    [NotifyPropertyChangedFor(nameof(IncrementDescription))]
    [NotifyPropertyChangedFor(nameof(IncrementHint))]
    [NotifyPropertyChangedFor(nameof(ActiveProgressTotal))]
    [NotifyPropertyChangedFor(nameof(HasKnownProgressTotal))]
    private int? _progressVolumes;

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
            var progress = ActiveProgress;
            if (progress is null)
            {
                return "-";
            }

            // Use ActiveProgressTotal (which falls back to NextAiringEpisode.Episode - 1 for
            // long-running airing shows) so the list display matches the Details page.
            var total = ActiveProgressTotal;
            return total is null ? $"{progress}" : $"{progress}/{total}";
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

    /// <inheritdoc />
    /// <remarks>
    /// <c>Media</c> is re-raised rather than <c>DisplayTitle</c> directly: the templates bind the
    /// nested path <c>Media.DisplayTitle</c>, and <c>Media</c> is a plain class that cannot notify
    /// for itself. The score members read <c>AppSettings.ScoreFormat</c> on this type, so they are
    /// raised by name.
    /// </remarks>
    public void RefreshDisplayProjections()
    {
        OnPropertyChanged(nameof(Media));
        OnPropertyChanged(nameof(ScoreDisplay));
        OnPropertyChanged(nameof(IsNumericScoreFormat));
    }

    /// <summary>
    /// True when this entry's progress is measured in volumes rather than chapters (#12).
    /// <para>
    /// AniList keeps <c>progress</c> (chapters) and <c>progressVolumes</c> as two independent
    /// counters and relates neither to the other nor to status, so picking one to drive the UI is
    /// a client decision. The rule here matches AniHyou's: chapters unless the entry has volume
    /// progress and no chapter progress. That reads however the user already tracks the title,
    /// and keeps exactly one counter, one total and one completion rule per entry.
    /// </para>
    /// <para>
    /// Derived rather than stored, which has one visible consequence: emptying the volume counter
    /// drops the entry back to chapters. Both read 0 at that point, so only the unit word changes.
    /// </para>
    /// </summary>
    public bool UsesVolumeProgress =>
        Media?.IsManga is true && ProgressVolumes is > 0 && Progress is null or 0;

    /// <summary>The unit <see cref="ActiveProgress"/> and <see cref="ActiveProgressTotal"/> are counted in.</summary>
    public MediaProgressUnit ActiveProgressUnit =>
        Media?.IsManga is not true ? MediaProgressUnit.Episode :
        UsesVolumeProgress ? MediaProgressUnit.Volume :
        MediaProgressUnit.Chapter;

    /// <summary>The counter currently driving the UI: volumes in volume mode, otherwise chapters/episodes.</summary>
    public int? ActiveProgress => UsesVolumeProgress ? ProgressVolumes : Progress;

    /// <summary>
    /// Cap for +1 / progress-slider logic, in <see cref="ActiveProgressUnit"/>.
    /// <para>
    /// For anime: the total episode count when known, otherwise the most-recently-aired episode
    /// (<c>NextAiringEpisode.Episode - 1</c>) so users of currently-airing shows stop at the latest
    /// episode they could have watched.
    /// </para>
    /// <para>
    /// For manga: the chapter or volume total, with no fallback — AniList publishes no
    /// chapter-release schedule, so a still-publishing series has no cap at all. It also returns
    /// null for both counts on every RELEASING series, which makes "no cap" the common manga case
    /// rather than the edge one.
    /// </para>
    /// </summary>
    public int? ActiveProgressTotal
    {
        get
        {
            if (Media?.IsManga is true)
            {
                // Never borrow the other unit's total: "3/141" for volume 3 of a 34-volume series
                // is worse than showing no total at all.
                var total = UsesVolumeProgress ? Media.Volumes : Media.Chapters;
                return total is > 0 ? total : null;
            }

            return Media?.Episodes is > 0 ? Media.Episodes :
                Media?.NextAiringEpisode?.Episode is > 1 ? Media.NextAiringEpisode.Episode - 1 :
                null;
        }
    }

    /// <summary>
    /// True only when a finite, declared total is known for the active unit. Used to gate the
    /// completion flow — long-running airing shows and still-publishing manga should not trigger
    /// completion when the cap is reached (they have no real cap to reach).
    /// </summary>
    public bool HasKnownProgressTotal =>
        Media?.IsManga is true
            ? (UsesVolumeProgress ? Media.Volumes : Media.Chapters) is > 0
            : Media?.Episodes is > 0;

    /// <summary>
    /// Writes <paramref name="value"/> to whichever counter is active, leaving the other one alone.
    /// Every progress surface goes through this rather than assigning <see cref="Progress"/>
    /// directly, so a volume-tracked entry can't have its chapter count quietly rewritten.
    /// </summary>
    public void SetActiveProgress(int value) => SetProgressFor(ActiveProgressUnit, value);

    /// <summary>Reads a specific counter, regardless of which one is currently active.</summary>
    public int? ProgressFor(MediaProgressUnit unit) =>
        unit == MediaProgressUnit.Volume ? ProgressVolumes : Progress;

    /// <summary>
    /// Writes a specific counter. Callers that need to undo their own change use this with the unit
    /// they captured before writing: <see cref="ActiveProgressUnit"/> is derived, so a write can
    /// move it, and a revert through <see cref="SetActiveProgress"/> could land on the other field.
    /// </summary>
    public void SetProgressFor(MediaProgressUnit unit, int? value)
    {
        if (unit == MediaProgressUnit.Volume)
        {
            ProgressVolumes = value;
        }
        else
        {
            Progress = value;
        }
    }

    /// <summary>
    /// Label for the +1 pill — "+1 EP", "+1 CH" or "+1 VOL" (#12). Per entry rather than per page,
    /// because the unit is decided per entry: two manga sitting in the same section can count
    /// different things.
    /// </summary>
    public string IncrementLabel => $"+1 {MediaListVocabulary.UnitAbbreviation(ActiveProgressUnit)}";

    /// <summary>
    /// Screen-reader text for the +1 pill, spelling out the same unit <see cref="IncrementLabel"/>
    /// abbreviates — "Increment chapter progress" rather than "+1 CH", which TalkBack would read
    /// letter by letter. Bound rather than hardcoded because the shared list view serves anime and
    /// manga alike (#12); a fixed "episode" here announced the wrong unit over a +1 VOL button.
    /// </summary>
    /// <remarks>
    /// <see cref="MediaListVocabulary.UnitNoun"/> is title-case because its other callers use it as
    /// a standalone label, so it is lowered here to read as a sentence — the same thing
    /// EditProgressPopup does with it.
    /// </remarks>
    public string IncrementDescription =>
        $"Increment {MediaListVocabulary.UnitNoun(ActiveProgressUnit).ToLowerInvariant()} progress";

    /// <summary>
    /// Screen-reader hint for the +1 pill, saying what pressing it does: "Adds one to the watched
    /// episode count", "…the read chapter count". Shares
    /// <see cref="IncrementDescription"/>'s reason for existing — it sat hardcoded to the anime
    /// wording beside it, and a manga entry announced the wrong verb as well as the wrong unit.
    /// </summary>
    public string IncrementHint =>
        $"Adds one to the {MediaListVocabulary.ConsumedVerb(ActiveProgressUnit)} " +
        $"{MediaListVocabulary.UnitNoun(ActiveProgressUnit).ToLowerInvariant()} count";

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
        && (ActiveProgressTotal is null || ActiveProgress < ActiveProgressTotal);

    /// <summary>
    /// True when progress is meaningful to edit from the long-press action menu — anything but a
    /// not-yet-started (<see cref="MediaListStatus.Planning"/>) or finished
    /// (<see cref="MediaListStatus.Completed"/>) show.
    /// </summary>
    public bool CanEditProgress =>
        Status is not (MediaListStatus.Planning or MediaListStatus.Completed);

    /// <summary>
    /// True when the entry can be marked completed from the long-press action menu: a finite total
    /// is known and it isn't already Completed. Intentionally allowed from any other list (not just
    /// Watching/Rewatching) — users forget to move a finished show out of Planning/Paused/etc.
    /// </summary>
    public bool CanMarkCompleted =>
        HasKnownProgressTotal && Status is not MediaListStatus.Completed;

    /// <summary>
    /// True when setting progress to <paramref name="progress"/> means the show is complete: a finite
    /// total is known, the value reaches it, and the entry isn't already Completed. Shared by the
    /// Details page and Library (+1 and Edit-progress) so "reaching the cap completes the show" is
    /// defined in exactly one place.
    /// </summary>
    public bool IsCompletionAt(int progress) =>
        HasKnownProgressTotal && ActiveProgressTotal is { } max && progress >= max && Status is not MediaListStatus.Completed;

    /// <summary>
    /// Clamps a candidate progress value to the valid range: <c>0</c> to <see cref="ActiveProgressTotal"/>
    /// (or unbounded above when the max is unknown). Shared by the progress-edit surfaces so the
    /// bounds are defined in one place.
    /// </summary>
    public int ClampProgress(int value) =>
        Math.Clamp(value, 0, ActiveProgressTotal ?? int.MaxValue);
}
