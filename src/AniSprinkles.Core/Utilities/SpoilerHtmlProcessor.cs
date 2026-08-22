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
            // Replace the whole spoiler span with a single small inline chip instead of
            // a long block bar. A long bar fragments the surrounding paragraph into a
            // "censored document" look; a compact "[spoiler]" tag preserves the shape of
            // the prose so the reader can still scan around it.
            return "<span style=\"color:#FF6B9D\">[spoiler]</span>";
        });
    }

    public static bool ContainsSpoilers(string? html)
    {
        return !string.IsNullOrEmpty(html) && SpoilerPattern.IsMatch(html);
    }
}
