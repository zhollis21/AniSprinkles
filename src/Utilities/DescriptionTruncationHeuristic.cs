using System.Net;

namespace AniSprinkles.Utilities;

/// <summary>
/// Estimates whether an HTML/markdown description will visually overflow the collapsed
/// line cap. Used to gate the "Read more" affordance so it only appears when the
/// rendered text actually exceeds <see cref="CollapsedMaxLines"/>. Constants mirror
/// MediaDetailsPageModel so behavior is consistent across details pages.
/// </summary>
public static class DescriptionTruncationHeuristic
{
    public const int CollapsedMaxLines = 8;

    // Approximate visible characters per line at 14sp on a typical phone (~360dp wide).
    private const int CharsPerLine = 45;

    // Even a short description with several paragraph breaks can spill past the line cap
    // because each break wraps onto multiple visual lines.
    private const int BreakCountThreshold = 3;

    public static bool IsTruncated(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        // Decode HTML entities first so &amp; (5 chars) counts as & (1 char).
        var decoded = WebUtility.HtmlDecode(description);

        var visibleChars = CountVisibleChars(decoded);
        if (visibleChars > CollapsedMaxLines * CharsPerLine)
        {
            return true;
        }

        var breakCount = CountSubstring(decoded, "<br") + CountSubstring(decoded, "</p>");
        return breakCount >= BreakCountThreshold;
    }

    private static int CountVisibleChars(string html)
    {
        var count = 0;
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) { count++; }
        }
        return count;
    }

    private static int CountSubstring(string s, string sub)
    {
        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(s))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = s.IndexOf(sub, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += sub.Length;
        }
        return count;
    }
}
