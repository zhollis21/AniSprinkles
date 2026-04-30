using System.Text.RegularExpressions;

namespace AniSprinkles.Utilities;

public static class SpoilerHtmlProcessor
{
    // ~!.+?!~ matches the AniList spoiler marker (non-greedy so multiple spoilers in
    // one description don't get coalesced). Singleline so spans can cross newlines.
    private static readonly Regex SpoilerPattern = new(
        @"~!(.*?)!~",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static string Process(string? html, bool reveal)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        if (reveal)
        {
            return SpoilerPattern.Replace(html, "$1");
        }

        return SpoilerPattern.Replace(html, m =>
        {
            // U+2588 (FULL BLOCK) renders as a uniform bar — slightly darker than surrounding
            // body text (Gray400 #919191) so it's visually distinct without screaming.
            // Android's Html.fromHtml is reliable for inline color via <span style="color:..."> on
            // API 24+; background-color is not always honoured, so we lean on a single-color bar.
            // Width scales with spoiler length but is clamped so a one-word spoiler stays visible
            // and a long sentence doesn't take an entire screen-line.
            var visibleLength = StripTags(m.Groups[1].Value).Length;
            var barLength = Math.Clamp(visibleLength / 2, 4, 40);
            var bar = new string('█', barLength);
            return $"<span style=\"color:#6E6E6E\">{bar}</span>";
        });
    }

    public static bool ContainsSpoilers(string? html)
    {
        return !string.IsNullOrEmpty(html) && SpoilerPattern.IsMatch(html);
    }

    private static string StripTags(string s)
    {
        // Cheap visible-character estimate: drop everything between < and >.
        // Avoids pulling in a full HTML parser for what's only a width hint.
        var sb = new System.Text.StringBuilder(s.Length);
        var inTag = false;
        foreach (var c in s)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) { sb.Append(c); }
        }
        return sb.ToString();
    }
}
