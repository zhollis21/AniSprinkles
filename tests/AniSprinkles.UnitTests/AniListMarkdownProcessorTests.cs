namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 small-helpers pass for <see cref="AniListMarkdownProcessor"/>. AniList descriptions mix HTML
/// with a bespoke Markdown flavour; MAUI's <c>Label.TextType="Html"</c> renders only the HTML half,
/// so anything this helper misses leaks to the screen verbatim — the reported symptom was
/// <c>__Height:__ 172 cm</c> rendering with literal underscores.
/// <para>
/// The ordering contract is the fragile part: bold has to run before italic, or the italic pattern
/// eats one asterisk of a <c>**bold**</c> pair and the output is mangled rather than merely
/// unformatted.
/// </para>
/// </summary>
public class AniListMarkdownProcessorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingInput_BecomesEmptyRatherThanNull(string? raw)
        => Assert.Equal(string.Empty, AniListMarkdownProcessor.Process(raw));

    // ── Emphasis ─────────────────────────────────────────────────────

    [Fact]
    public void DoubleUnderscore_BecomesBold()
        => Assert.Equal("<b>Height:</b> 172 cm", AniListMarkdownProcessor.Process("__Height:__ 172 cm"));

    [Fact]
    public void DoubleAsterisk_BecomesBold()
        => Assert.Equal("<b>Height:</b> 172 cm", AniListMarkdownProcessor.Process("**Height:** 172 cm"));

    [Fact]
    public void SingleAsterisk_BecomesItalic()
        => Assert.Equal("an <i>emphatic</i> word", AniListMarkdownProcessor.Process("an *emphatic* word"));

    [Fact]
    public void BoldRunsBeforeItalic_SoADoubleAsteriskPairIsNotEatenAsItalics()
    {
        // The ordering guard: if italic ran first this would come out as <i>*bold*</i>.
        var result = AniListMarkdownProcessor.Process("**bold**");

        Assert.Equal("<b>bold</b>", result);
        Assert.DoesNotContain("<i>", result);
        Assert.DoesNotContain("*", result);
    }

    [Fact]
    public void BoldAndItalicInTheSameLine_BothSurvive()
        => Assert.Equal(
            "<b>Name:</b> the <i>real</i> one",
            AniListMarkdownProcessor.Process("**Name:** the *real* one"));

    [Fact]
    public void Strikethrough_BecomesAnSTag()
        => Assert.Equal("<s>gone</s>", AniListMarkdownProcessor.Process("~~gone~~"));

    // ── Links ────────────────────────────────────────────────────────

    [Fact]
    public void AMarkdownLink_BecomesAnAnchor()
        => Assert.Equal(
            "see <a href=\"https://anilist.co/anime/1\">the entry</a>",
            AniListMarkdownProcessor.Process("see [the entry](https://anilist.co/anime/1)"));

    [Fact]
    public void ALinkToANonWebScheme_IsLeftAlone()
        // The pattern is deliberately bounded to http(s); anything else is not something the label
        // should turn into a tappable anchor.
        => Assert.Equal("[a file](ftp://example.com/x)", AniListMarkdownProcessor.Process("[a file](ftp://example.com/x)"));

    // ── Embedded media is stripped, not rendered ─────────────────────

    [Fact]
    public void AnInlineImage_IsStripped()
        => Assert.Equal("before  after", AniListMarkdownProcessor.Process("before img(https://x/y.png) after"));

    [Fact]
    public void ASizedInlineImage_IsAlsoStripped()
        // AniList writes the display width into the tag, e.g. img250(...).
        => Assert.Equal("before  after", AniListMarkdownProcessor.Process("before img250(https://x/y.png) after"));

    [Fact]
    public void AnInlineYoutubeEmbed_IsStripped()
        => Assert.Equal("watch:", AniListMarkdownProcessor.Process("watch: youtube(https://youtu.be/abc)"));

    [Fact]
    public void ImageStrippingIsCaseInsensitive()
        => Assert.Equal("x", AniListMarkdownProcessor.Process("IMG(https://x/y.png)x"));

    // ── Whitespace ───────────────────────────────────────────────────

    [Fact]
    public void RunsOfBlankLines_CollapseToASingleBlankLine()
    {
        // Long AniList wiki entries otherwise blow out the description card's height.
        var result = AniListMarkdownProcessor.Process("one\n\n\n\n\ntwo");

        Assert.Equal("one\n\ntwo", result);
    }

    [Fact]
    public void ASingleBlankLine_IsPreserved()
        => Assert.Equal("one\n\ntwo", AniListMarkdownProcessor.Process("one\n\ntwo"));

    [Fact]
    public void SurroundingWhitespace_IsTrimmed()
        => Assert.Equal("body", AniListMarkdownProcessor.Process("\n\n  body  \n\n"));

    // ── HTML already in the description is left for the label ────────

    [Fact]
    public void ExistingHtml_PassesThroughUntouched()
        => Assert.Equal("<i>already</i> html", AniListMarkdownProcessor.Process("<i>already</i> html"));

    [Fact]
    public void AMixedDescription_ComesOutFullyHtml()
    {
        var result = AniListMarkdownProcessor.Process(
            "__Height:__ 172 cm\nimg(https://x/y.png)\n\n\n\nSee [more](https://anilist.co/x).");

        // Stripping the image joins the newline before it to the four after it, and the run then
        // collapses to a single blank line.
        Assert.Equal("<b>Height:</b> 172 cm\n\nSee <a href=\"https://anilist.co/x\">more</a>.", result);
    }
}
