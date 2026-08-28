using System.Net;

namespace AniSprinkles.Utilities;

/// <summary>
/// Estimates whether an HTML/markdown description will visually overflow the collapsed
/// line cap. Used to gate the "Read more" affordance so it only appears when the
/// rendered text actually exceeds <see cref="CollapsedMaxLines"/>.
/// <para>
/// The one estimate for all three details pages — media, character and staff. Their description
/// labels are identical (<c>Body2</c>, <c>MaxLines="{Binding DescriptionMaxLines}"</c>,
/// <c>LineBreakMode="TailTruncation"</c>), so a second copy could only ever drift, which is what a
/// duplicate on <c>MediaDetailsPageModel</c> did until #138.
/// </para>
/// <para>
/// Feed it the string the label will actually render, not the source markdown: on the character and
/// staff pages that means the output of <see cref="AniListMarkdownProcessor"/>, since raw AniList
/// markdown counts link URLs as visible text and carries none of the break tags this counts.
/// </para>
/// </summary>
public static class DescriptionTruncationHeuristic
{
    public const int CollapsedMaxLines = 8;

    // Approximate visible characters per line at 15sp — the Body2 size the description labels
    // actually render at — on a typical phone (~360dp wide).
    //
    // Deliberately conservative. A character count can't know about word wrapping, long words or
    // the reader's font-scale setting, so it will be wrong; the two directions just aren't equally
    // bad. Over-stating capacity means no "Read more" appears while the label clamps and
    // tail-truncates anyway, and the rest of the bio is unreachable. Under-stating it means a
    // redundant "Read more" on text that already fits, which costs the reader nothing. So the
    // estimate errs low.
    private const int CharsPerLine = 40;

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
