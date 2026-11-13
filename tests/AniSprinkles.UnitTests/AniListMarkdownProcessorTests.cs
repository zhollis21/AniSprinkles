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
/// <para>
/// Newline handling is the other contract (#138). Character and staff descriptions arrive with bare
/// newlines and no <c>&lt;br&gt;</c>, and <c>Html.fromHtml</c> treats a bare newline as a space — so
/// every break has to leave here as markup or the whole bio renders as one block.
/// </para>
/// </summary>
public class AniListMarkdownProcessorTests
{
    [TestCase(null)]
    [TestCase("")]
    public void MissingInput_BecomesEmptyRatherThanNull(string? raw)
        => Assert.That(AniListMarkdownProcessor.Process(raw), Is.EqualTo(string.Empty));

    // ── Emphasis ─────────────────────────────────────────────────────

    [Test]
    public void DoubleUnderscore_BecomesBold()
        => Assert.That(AniListMarkdownProcessor.Process("__Height:__ 172 cm"), Is.EqualTo("<b>Height:</b> 172 cm"));

    [Test]
    public void DoubleAsterisk_BecomesBold()
        => Assert.That(AniListMarkdownProcessor.Process("**Height:** 172 cm"), Is.EqualTo("<b>Height:</b> 172 cm"));

    [Test]
    public void SingleAsterisk_BecomesItalic()
        => Assert.That(AniListMarkdownProcessor.Process("an *emphatic* word"), Is.EqualTo("an <i>emphatic</i> word"));

    [Test]
    public void BoldRunsBeforeItalic_SoADoubleAsteriskPairIsNotEatenAsItalics()
    {
        // The ordering guard: if italic ran first this would come out as <i>*bold*</i>.
        var result = AniListMarkdownProcessor.Process("**bold**");

        Assert.That(result, Is.EqualTo("<b>bold</b>"));
        Assert.That(result, Does.Not.Contain("<i>"));
        Assert.That(result, Does.Not.Contain("*"));
    }

    [Test]
    public void BoldAndItalicInTheSameLine_BothSurvive()
        => Assert.That(
            AniListMarkdownProcessor.Process("**Name:** the *real* one"),
            Is.EqualTo("<b>Name:</b> the <i>real</i> one"));

    [Test]
    public void SingleUnderscore_BecomesItalic()
        // Character 17 renders "(a _Jinchuuriki_)" with the underscores showing. 9 of 200 sampled
        // bios use this form, Frieren's and four staff among them.
        => Assert.That(AniListMarkdownProcessor.Process("a _Jinchuuriki_"), Is.EqualTo("a <i>Jinchuuriki</i>"));

    [Test]
    public void UnderscoresInsideAWord_AreLeftAlone()
        // The reason this pattern needs boundaries: identifiers and file names carry underscores
        // that are not emphasis, and pairing them up would eat the text between.
        => Assert.That(AniListMarkdownProcessor.Process("snake_case_name"), Is.EqualTo("snake_case_name"));

    [Test]
    public void UnderscoreItalicRunsAfterBold_SoItDoesNotEatTheBoldMarkers()
    {
        // Same ordering contract as the asterisks: if the single-underscore pattern ran first it
        // would match the inner pair of __bold__ and leave a stray underscore either side.
        var result = AniListMarkdownProcessor.Process("__Height:__ 172 cm, a _tall_ one");

        Assert.That(result, Is.EqualTo("<b>Height:</b> 172 cm, a <i>tall</i> one"));
        Assert.That(result, Does.Not.Contain("_"));
    }

    [Test]
    public void AnUnderscoreItalicSpanningPunctuation_IsStillMatched()
        // Staff bios italicise show titles, which carry spaces and symbols.
        => Assert.That(
            AniListMarkdownProcessor.Process("in _Uta no☆Prince-sama♪_ today"),
            Is.EqualTo("in <i>Uta no☆Prince-sama♪</i> today"));

    [Test]
    public void Strikethrough_BecomesAnSTag()
        => Assert.That(AniListMarkdownProcessor.Process("~~gone~~"), Is.EqualTo("<s>gone</s>"));

    // ── Links ────────────────────────────────────────────────────────

    [Test]
    public void AMarkdownLink_BecomesAnAnchor()
        => Assert.That(
            AniListMarkdownProcessor.Process("see [the entry](https://anilist.co/anime/1)"),
            Is.EqualTo("see <a href=\"https://anilist.co/anime/1\">the entry</a>"));

    [Test]
    public void ALinkToANonWebScheme_IsLeftAlone()
        // The pattern is deliberately bounded to http(s); anything else is not something the label
        // should turn into a tappable anchor.
        => Assert.That(AniListMarkdownProcessor.Process("[a file](ftp://example.com/x)"), Is.EqualTo("[a file](ftp://example.com/x)"));

    // ── Embedded media is stripped, not rendered ─────────────────────

    [Test]
    public void AnInlineImage_IsStripped()
        => Assert.That(AniListMarkdownProcessor.Process("before img(https://x/y.png) after"), Is.EqualTo("before  after"));

    [Test]
    public void ASizedInlineImage_IsAlsoStripped()
        // AniList writes the display width into the tag, e.g. img250(...).
        => Assert.That(AniListMarkdownProcessor.Process("before img250(https://x/y.png) after"), Is.EqualTo("before  after"));

    [Test]
    public void AnInlineYoutubeEmbed_IsStripped()
        => Assert.That(AniListMarkdownProcessor.Process("watch: youtube(https://youtu.be/abc)"), Is.EqualTo("watch:"));

    [Test]
    public void ImageStrippingIsCaseInsensitive()
        => Assert.That(AniListMarkdownProcessor.Process("IMG(https://x/y.png)x"), Is.EqualTo("x"));

    // ── Newlines become markup (#138) ────────────────────────────────
    //
    // These use explicit escapes rather than raw string literals on purpose: *.cs is checked out
    // CRLF in this repo, so a raw literal would silently test only the \r\n path, and the string
    // this helper actually receives on device is LF (DescriptionParser builds it with AppendLine,
    // which is Environment.NewLine — \n on Android, \r\n under the Windows test run).

    [Test]
    public void ABlankLine_BecomesAParagraphBreak()
        => Assert.That(AniListMarkdownProcessor.Process("one\n\ntwo"), Is.EqualTo("one<br><br>two"));

    [Test]
    public void ASingleNewline_BecomesALineBreak()
        // AniList does not hard-wrap prose, so a lone newline is always a break the author meant —
        // the bullet lists in staff bios depend on this.
        => Assert.That(AniListMarkdownProcessor.Process("one\ntwo"), Is.EqualTo("one<br>two"));

    [Test]
    public void ACarriageReturnBlankLine_BecomesAParagraphBreak()
        => Assert.That(AniListMarkdownProcessor.Process("one\r\n\r\ntwo"), Is.EqualTo("one<br><br>two"));

    [Test]
    public void ACarriageReturnSingleNewline_BecomesALineBreak()
        => Assert.That(AniListMarkdownProcessor.Process("one\r\ntwo"), Is.EqualTo("one<br>two"));

    [Test]
    public void RunsOfBlankLines_CollapseToASingleParagraphBreak()
    {
        // Long AniList wiki entries otherwise blow out the description card's height.
        var result = AniListMarkdownProcessor.Process("one\n\n\n\n\ntwo");

        Assert.That(result, Is.EqualTo("one<br><br>two"));
    }

    [Test]
    public void TrailingSpacesBeforeABreak_AreNotCarriedIntoTheMarkup()
        // AniList writes the Markdown hard-break convention (two trailing spaces) before its
        // paragraph breaks; those would otherwise survive as a stray gap before the <br>.
        => Assert.That(AniListMarkdownProcessor.Process("one.  \n\ntwo"), Is.EqualTo("one.<br><br>two"));

    [Test]
    public void SurroundingWhitespace_IsTrimmedRatherThanTurnedIntoBreaks()
        => Assert.That(AniListMarkdownProcessor.Process("\n\n  body  \n\n"), Is.EqualTo("body"));

    [Test]
    public void AStaffTriviaList_KeepsALineBreakPerBullet()
    {
        // Staff 96881 (Eiichiro Oda) in miniature. Mapping lone newlines to spaces would run all
        // three bullets together into one line — the case that decided #138 against that mapping.
        var result = AniListMarkdownProcessor.Process("__Trivia:__\n- Married Chiaki Inaba.\n- Loves Lupin III.");

        Assert.That(result, Is.EqualTo("<b>Trivia:</b><br>- Married Chiaki Inaba.<br>- Loves Lupin III."));
    }

    // ── HTML already in the description is left for the label ────────

    [Test]
    public void ExistingHtml_PassesThroughUntouched()
        => Assert.That(AniListMarkdownProcessor.Process("<i>already</i> html"), Is.EqualTo("<i>already</i> html"));

    [Test]
    public void AMixedDescription_ComesOutFullyHtml()
    {
        var result = AniListMarkdownProcessor.Process(
            "__Height:__ 172 cm\nimg(https://x/y.png)\n\n\n\nSee [more](https://anilist.co/x).");

        // Stripping the image leaves the newline before it adjacent to the four after it; the run
        // collapses to one blank line, which then becomes the paragraph break.
        Assert.That(
            result,
            Is.EqualTo("<b>Height:</b> 172 cm<br><br>See <a href=\"https://anilist.co/x\">more</a>."));
    }
}
