using System.Globalization;
using System.Text;
using AniSprinkles.Utilities;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.Models;

public class RelatedMedia
{
    public int Id { get; set; }
    public MediaTitle? Title { get; set; }
    public string? Format { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public MediaCoverImage? CoverImage { get; set; }
    public int? AverageScore { get; set; }
    public int? Favourites { get; set; }
    public int? Popularity { get; set; }
    public int? Trending { get; set; }
    public MediaDate? StartDate { get; set; }
    public int? Episodes { get; set; }

    // Viewer's list-entry snapshot (Discover/browse/search queries request mediaListEntry when
    // authenticated). Null in every other query, which keeps existing carousels untouched.
    public int? ListEntryId { get; set; }
    public MediaListStatus? ListStatus { get; set; }
    public int? ListProgress { get; set; }
    public double? ListScore { get; set; }

    // Was its own copy of the chain, falling back to "Unknown" where Media said "Unknown Title" —
    // the drift that motivated consolidating this (#141).
    public string DisplayTitle
        => TitleSelector.Select(AppSettings.TitleLanguage, Title?.Romaji, Title?.English, Title?.Native);

    /// <summary>Human-readable format for list cards: AniList's <c>TV_SHORT</c> → "TV SHORT". Blank when absent.</summary>
    public string FormatDisplay => Format?.Replace("_", " ") ?? "";

    /// <summary>
    /// True when this entry is an anime. The media detail screen queries
    /// <c>Media(id:, type: ANIME)</c>, so navigating to a non-anime id (manga/novel) returns 404.
    /// Tile navigation gates on this to show a "not supported" toast instead of a doomed fetch.
    /// </summary>
    public bool IsAnime => string.Equals(Type, "ANIME", StringComparison.OrdinalIgnoreCase);

    public bool HasScore => AverageScore is > 0;
    public bool HasFavourites => Favourites is > 0;
    public bool HasPopularity => Popularity is > 0;
    public bool HasYear => StartDate?.Year is > 0;

    // AniList stores scores 0–100; render as a 0–10.0 rating since that scale is more recognizable.
    public string ScoreDisplay => HasScore
        ? (AverageScore!.Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture)
        : string.Empty;

    public string FavouritesDisplay => MetricFormat.Compact(Favourites);

    public string PopularityDisplay => MetricFormat.Compact(Popularity);

    public string YearDisplay => HasYear ? StartDate!.Year!.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    // Sort-metric fallbacks: when a list is sorted by a metric, the badge must still show (never blank, which
    // looks broken). Counts read "0"; a year or rating reads "—" since 0 would misrepresent "unknown".
    public string FavouritesOrZero => MetricFormat.CompactOrZero(Favourites);

    public string PopularityOrZero => MetricFormat.CompactOrZero(Popularity);

    public string TrendingOrZero => MetricFormat.CompactOrZero(Trending);

    public string ScoreOrDash => HasScore ? ScoreDisplay : "—";

    public string YearOrDash => HasYear ? YearDisplay : "—";

    /// <summary>AniList's <c>NOT_YET_RELEASED</c> → "Not Yet Released". Blank when absent.</summary>
    public string MediaStatusDisplay => string.IsNullOrEmpty(Status)
        ? string.Empty
        : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Status.Replace('_', ' ').ToLowerInvariant());

    /// <summary>Single-line metadata for browse/search rows: "TV · 2026 · Releasing" (absent parts omitted).
    /// Hand-composed (no LINQ/array) — this is evaluated per visible row while scrolling.</summary>
    public string BrowseMetaDisplay
    {
        get
        {
            var sb = new StringBuilder();
            AppendMetaPart(sb, FormatDisplay);
            AppendMetaPart(sb, YearDisplay);
            AppendMetaPart(sb, MediaStatusDisplay);
            return sb.ToString();
        }
    }

    private static void AppendMetaPart(StringBuilder sb, string part)
    {
        if (string.IsNullOrEmpty(part))
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append(" · ");
        }

        sb.Append(part);
    }

    public bool HasListStatus => ListStatus is not null;

    /// <summary>Friendly list-status label for chips ("Watching"/"Rewatching" instead of the raw enum names).</summary>
    public string ListStatusDisplay => ListStatus switch
    {
        MediaListStatus.Current => "Watching",
        MediaListStatus.Repeating => "Rewatching",
        { } status => status.ToString(),
        null => string.Empty,
    };

    public Color ListStatusColor => ListStatus switch
    {
        MediaListStatus.Current => Color.FromArgb("#00C2FF"),
        MediaListStatus.Planning => Color.FromArgb("#4E7CFF"),
        MediaListStatus.Completed => Color.FromArgb("#34C759"),
        MediaListStatus.Paused => Color.FromArgb("#FF9500"),
        MediaListStatus.Dropped => Color.FromArgb("#FF3B30"),
        MediaListStatus.Repeating => Color.FromArgb("#AF52DE"),
        _ => Colors.Transparent,
    };
}
