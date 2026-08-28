using System.Text.RegularExpressions;

namespace AniSprinkles.Utilities;

/// <summary>
/// AniList descriptions are a mix of HTML and a bespoke flavor of Markdown
/// (<c>__bold__</c>, <c>**bold**</c>, <c>*italic*</c>, <c>[text](url)</c>, <c>~~strikethrough~~</c>,
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

    // Bounded to a single line and fenced by non-word lookarounds, because an underscore is only
    // emphasis when it isn't inside a word — snake_case identifiers and file names are common
    // enough in these bios that an unfenced pair would swallow the text between them.
    private static readonly Regex ItalicUnderscore = new(@"(?<![\w_])_([^_\n]+?)_(?![\w_])", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[(.+?)\]\((https?://[^\s)]+)\)", RegexOptions.Compiled);
    private static readonly Regex InlineImage = new(@"img\d*\(([^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineYoutube = new(@"youtube\(([^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Strikethrough = new(@"~~(.+?)~~", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex MultipleNewlines = new(@"(\r?\n){3,}", RegexOptions.Compiled);

    // Both swallow the horizontal whitespace around the break: AniList writes the Markdown
    // hard-break convention (two trailing spaces) before its paragraph breaks, which would
    // otherwise survive as a stray gap ahead of the <br>. Paragraph runs first so a blank line
    // isn't consumed as two separate single breaks.
    private static readonly Regex ParagraphBreak = new(@"[ \t]*\r?\n[ \t]*\r?\n[ \t]*", RegexOptions.Compiled);
    private static readonly Regex LineBreak = new(@"[ \t]*\r?\n[ \t]*", RegexOptions.Compiled);

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
        s = ItalicUnderscore.Replace(s, "<i>$1</i>");
        s = Strikethrough.Replace(s, "<s>$1</s>");

        // [text](url) → anchor. We don't render colour here; the surrounding label TextColor wins.
        s = Link.Replace(s, "<a href=\"$2\">$1</a>");

        // Collapse runs of >2 blank lines so massive AniList wikis don't blow out the card height.
        s = MultipleNewlines.Replace(s, "\n\n");

        // Trim before converting, or the leading and trailing newlines become stray breaks.
        s = s.Trim();

        // Character and staff descriptions arrive as bare newlines with no <br> or <p> of their own
        // (media descriptions are the opposite, which is why only these two page types lost their
        // shape). Html.fromHtml applies HTML whitespace rules, where a bare newline is just a space,
        // so every break has to leave here as markup or the bio renders as one wall of prose.
        //
        // Every newline becomes a break, including lone ones: AniList doesn't hard-wrap prose —
        // across the 50 most-favourited characters and staff, not one lone newline fell
        // mid-sentence — so a lone newline is always structure the author meant, most visibly the
        // bullet lists in staff bios.
        s = ParagraphBreak.Replace(s, "<br><br>");
        s = LineBreak.Replace(s, "<br>");

        return s;
    }
}
