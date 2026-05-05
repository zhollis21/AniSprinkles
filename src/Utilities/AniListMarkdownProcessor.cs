using System.Text.RegularExpressions;

namespace AniSprinkles.Utilities;

/// <summary>
/// AniList descriptions are a mix of HTML and a bespoke flavor of Markdown
/// (<c>__bold__</c>, <c>**bold**</c>, <c>*italic*</c>, <c>[text](url)</c>, <c>~~~strikethrough~~~</c>,
/// <c>img(url)</c>, plus AniList's <c>~!spoiler!~</c> form). MAUI <c>Label.TextType="Html"</c>
/// only renders the HTML half, so the Markdown leftovers leak through verbatim — for example
/// <c>__Height:__ 172 cm</c> shows literal underscores. This helper rewrites the Markdown
/// fragments into their HTML equivalents (or strips them) before the spoiler processor runs,
/// so the final string is something <c>Html.fromHtml</c> can render cleanly.
/// </summary>
public static class AniListMarkdownProcessor
{
    // Order matters: bold (** or __) must run before italic (* or _) so we don't eat the
    // outer markers as italics first. Each pattern is non-greedy and bounded to a single
    // line where appropriate to avoid swallowing across paragraphs.
    private static readonly Regex BoldDoubleUnderscore = new(@"__(.+?)__", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BoldDoubleAsterisk = new(@"\*\*(.+?)\*\*", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ItalicAsterisk = new(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[(.+?)\]\((https?://[^\s)]+)\)", RegexOptions.Compiled);
    private static readonly Regex InlineImage = new(@"img\d*\(([^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineYoutube = new(@"youtube\(([^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Strikethrough = new(@"~~(.+?)~~", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex MultipleNewlines = new(@"(\r?\n){3,}", RegexOptions.Compiled);

    public static string Process(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var s = raw;

        // Strip embedded media — mobile description card has no room for inline pics/videos.
        s = InlineImage.Replace(s, string.Empty);
        s = InlineYoutube.Replace(s, string.Empty);

        // Bold runs first so the inner italic regex doesn't eat the markers.
        s = BoldDoubleUnderscore.Replace(s, "<b>$1</b>");
        s = BoldDoubleAsterisk.Replace(s, "<b>$1</b>");
        s = ItalicAsterisk.Replace(s, "<i>$1</i>");
        s = Strikethrough.Replace(s, "<s>$1</s>");

        // [text](url) → anchor. We don't render colour here; the surrounding label TextColor wins.
        s = Link.Replace(s, "<a href=\"$2\">$1</a>");

        // Collapse runs of >2 blank lines so massive AniList wikis don't blow out the card height.
        s = MultipleNewlines.Replace(s, "\n\n");

        return s.Trim();
    }
}
