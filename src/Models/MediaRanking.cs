using System.Globalization;

namespace AniSprinkles.Models;

public class MediaRanking
{
    public int? Rank { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }
    public int? Year { get; set; }
    public string? Season { get; set; }
    public bool? AllTime { get; set; }
    public string? Context { get; set; }

    /// <summary>The type label like "most popular" or "highest rated" (stripped of scope qualifiers).</summary>
    public string TypeLabel
    {
        get
        {
            var ctx = Context ?? "unknown";
            // AniList embeds scope in context (e.g. "highest rated all time") — strip it
            return ctx
                .Replace(" all time", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }
    }

    /// <summary>The scope key used for grouping (e.g. "Fall 2025", "2025", "All Time").</summary>
    public string ScopeKey
    {
        get
        {
            if (AllTime == true)
            {
                return "All Time";
            }

            if (Season is not null && Year is not null)
            {
                var ti = CultureInfo.InvariantCulture.TextInfo;
                return $"{ti.ToTitleCase(Season.ToLowerInvariant())} {Year}";
            }

            if (Year is not null)
            {
                return Year.Value.ToString(CultureInfo.InvariantCulture);
            }

            return "Other";
        }
    }
}

public class RankingGroup
{
    public string Title { get; set; } = "";
    public List<MediaRanking> Items { get; set; } = [];
}
