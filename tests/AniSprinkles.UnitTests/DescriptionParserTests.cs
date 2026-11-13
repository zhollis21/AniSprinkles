namespace AniSprinkles.UnitTests;

public class DescriptionParserTests
{
    [Test]
    public void Parse_Null_ReturnsEmpty()
    {
        var result = DescriptionParser.Parse(null);
        Assert.That(result.Stats, Is.Empty);
        Assert.That(result.Prose, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Parse_PureProse_ReturnsAsProse()
    {
        var result = DescriptionParser.Parse("She was born in a dairy farm.\nShe became a mangaka.");
        Assert.That(result.Stats, Is.Empty);
        Assert.That(result.Prose, Does.Contain("dairy farm"));
    }

    [Test]
    public void Parse_LuffyShape_ExtractsStatRowsAndProse()
    {
        var input = """
            __Height:__ 172 cm
            __Affiliations:__ ~!Straw Hat Pirates (Captain); Four Emperors!~
            __Devil Fruit:__ Gomu Gomu no Mi
            ~!__True Devil Fruit:__ Hito Hito no Mi Model: Nika!~
            __Bounty:__ ~!3,000,000,000!~

            Luffy is the captain of the Straw Hat Pirates.
            """;

        var result = DescriptionParser.Parse(input);

        Assert.That(result.Stats.Count, Is.EqualTo(5));

        Assert.That(result.Stats[0].Label, Is.EqualTo("Height"));
        Assert.That(result.Stats[0].Value, Is.EqualTo("172 cm"));
        Assert.That(result.Stats[0].IsRowSpoiler, Is.False);
        Assert.That(result.Stats[0].IsValueSpoiler, Is.False);

        Assert.That(result.Stats[1].Label, Is.EqualTo("Affiliations"));
        Assert.That(result.Stats[1].Value, Is.EqualTo("Straw Hat Pirates (Captain); Four Emperors"));
        Assert.That(result.Stats[1].IsRowSpoiler, Is.False);
        Assert.That(result.Stats[1].IsValueSpoiler, Is.True);

        Assert.That(result.Stats[2].Label, Is.EqualTo("Devil Fruit"));

        Assert.That(result.Stats[3].Label, Is.EqualTo("True Devil Fruit"));
        Assert.That(result.Stats[3].Value, Is.EqualTo("Hito Hito no Mi Model: Nika"));
        Assert.That(result.Stats[3].IsRowSpoiler, Is.True);

        Assert.That(result.Stats[4].Label, Is.EqualTo("Bounty"));
        Assert.That(result.Stats[4].IsValueSpoiler, Is.True);

        Assert.That(result.Prose, Does.Contain("Luffy is the captain"));
    }

    [Test]
    public void Parse_BlankLineEndsStats_ProseAfterIsCollected()
    {
        var input = "__Height:__ 172 cm\n\nThe rest is prose.\n__This:__ is not a stat row anymore.";
        var result = DescriptionParser.Parse(input);

        Assert.That(result.Stats, Has.Exactly(1).Items);
        Assert.That(result.Stats[0].Label, Is.EqualTo("Height"));
        // Once prose starts, subsequent stat-shaped lines stay in prose to avoid splitting context.
        Assert.That(result.Prose, Does.Contain("The rest is prose"));
        Assert.That(result.Prose, Does.Contain("__This:__"));
    }

    [Test]
    public void Parse_NonStatLineFirst_AllGoesToProse()
    {
        var input = "Just some prose.\n__Trailing:__ stat that won't be extracted.";
        var result = DescriptionParser.Parse(input);
        Assert.That(result.Stats, Is.Empty);
        Assert.That(result.Prose, Does.Contain("Just some prose"));
        Assert.That(result.Prose, Does.Contain("__Trailing:__"));
    }

    [Test]
    public void Parse_LabelWithSpaces_IsAccepted()
    {
        var result = DescriptionParser.Parse("__Devil Fruit Type:__ Paramecia");
        Assert.That(result.Stats, Has.Exactly(1).Items);
        Assert.That(result.Stats[0].Label, Is.EqualTo("Devil Fruit Type"));
    }

    [Test]
    public void Parse_EmptyLabel_DropsTheLine()
    {
        var result = DescriptionParser.Parse("__:__ value");
        Assert.That(result.Stats, Is.Empty);
        Assert.That(result.Prose, Does.Contain("__:__ value"));
    }

    [Test]
    public void Parse_BoldOnlyNoColon_IsProse()
    {
        // __Some bold text__ without a colon shouldn't be parsed as a stat.
        var result = DescriptionParser.Parse("__Some bold text__\nNot a stat row.");
        Assert.That(result.Stats, Is.Empty);
        Assert.That(result.Prose, Does.Contain("__Some bold text__"));
    }

    [Test]
    public void Parse_LabelWithTheColonOutsideTheUnderscores_IsStillAStatRow()
    {
        // Character 17's real shape. AniList editors write the label both ways, and only
        // __Label:__ parsed — so __Height__: failed on the *first* line, which latches foundProse
        // and stops stat parsing for the rest of the description. The valid __Family:__ row below
        // it was lost too, and the whole stat card disappeared into the prose. 5 of 200 sampled
        // characters and staff open this way, Naruto, Sasuke, Kurapika and Joseph Joestar included.
        var input = "__Height__: 145-180 cm\n"
            + "__Family:__ ~!Minato (father)!~\n"
            + "\n"
            + "Born in Konohagakure.";

        var result = DescriptionParser.Parse(input);

        Assert.That(result.Stats.Count, Is.EqualTo(2));
        Assert.That(result.Stats[0].Label, Is.EqualTo("Height"));
        Assert.That(result.Stats[0].Value, Is.EqualTo("145-180 cm"));
        Assert.That(result.Stats[1].Label, Is.EqualTo("Family"));
        Assert.That(result.Stats[1].IsValueSpoiler, Is.True);
        Assert.That(result.Prose, Is.EqualTo("Born in Konohagakure."));
    }

    [Test]
    public void Parse_BoldRunWithNoColonEitherSideOfTheUnderscores_IsStillProse()
        // The guard on the above: widening the pattern must not start treating an ordinary bold
        // run as a stat row.
        => Assert.That(DescriptionParser.Parse("__Some bold text__\nNot a stat row.").Stats, Is.Empty);

    [Test]
    public void Parse_SpoilerBlockSpanningTwoLines_KeepsBothRowsInStats()
    {
        // Character 40's real shape: AniList wraps two consecutive stat rows in one ~!…!~ block,
        // opening on one line and closing on the next. Handling only the single-line form made the
        // opening line fail to parse, which ended the stats section early and dropped every row
        // after it — including the un-spoilered Bounty — into the prose card.
        var input = "__Height:__ 172 cm\n"
            + "~!__True Devil Fruit:__ Hito Hito no Mi Model: Nika\n"
            + "__True Devil Fruit Type:__ Mythical Zoan!~\n"
            + "__Bounty:__ ~!3,000,000,000!~\n"
            + "\n"
            + "Luffy is the captain of the Straw Hat Pirates.";

        var result = DescriptionParser.Parse(input);

        Assert.That(result.Stats.Count, Is.EqualTo(4));

        Assert.That(result.Stats[1].Label, Is.EqualTo("True Devil Fruit"));
        Assert.That(result.Stats[1].Value, Is.EqualTo("Hito Hito no Mi Model: Nika"));
        Assert.That(result.Stats[1].IsRowSpoiler, Is.True);

        Assert.That(result.Stats[2].Label, Is.EqualTo("True Devil Fruit Type"));
        Assert.That(result.Stats[2].Value, Is.EqualTo("Mythical Zoan"));
        Assert.That(result.Stats[2].IsRowSpoiler, Is.True);

        // The block closed, so the row after it is read normally rather than as part of it.
        Assert.That(result.Stats[3].Label, Is.EqualTo("Bounty"));
        Assert.That(result.Stats[3].IsRowSpoiler, Is.False);
        Assert.That(result.Stats[3].IsValueSpoiler, Is.True);

        Assert.That(result.Prose, Is.EqualTo("Luffy is the captain of the Straw Hat Pirates."));
        Assert.That(result.Prose, Does.Not.Contain("True Devil Fruit"));
    }

    [Test]
    public void Parse_SpoilerBlockThatNeverCloses_StillKeepsItsRowsAsSpoilers()
    {
        // Defensive: an unterminated block shouldn't spill the rest of the stats into prose either.
        var input = "__A:__ one\n~!__B:__ two\n__C:__ three";

        var result = DescriptionParser.Parse(input);

        Assert.That(result.Stats.Count, Is.EqualTo(3));
        Assert.That(result.Stats[0].IsRowSpoiler, Is.False);
        Assert.That(result.Stats[1].IsRowSpoiler, Is.True);
        Assert.That(result.Stats[2].IsRowSpoiler, Is.True);
    }
}

public class DescriptionTruncationHeuristicTests
{
    [Test]
    public void IsTruncated_NullOrEmpty_False()
    {
        Assert.That(DescriptionTruncationHeuristic.IsTruncated(null), Is.False);
        Assert.That(DescriptionTruncationHeuristic.IsTruncated(""), Is.False);
        Assert.That(DescriptionTruncationHeuristic.IsTruncated("   "), Is.False);
    }

    [Test]
    public void IsTruncated_ShortText_False()
    {
        Assert.That(DescriptionTruncationHeuristic.IsTruncated("Just one short sentence."), Is.False);
    }

    [Test]
    public void IsTruncated_VisibleCharOverflow_True()
    {
        // 8 lines * 40 chars = 320. Beyond that should trip the visible-char rule.
        var text = new string('a', 500);
        Assert.That(DescriptionTruncationHeuristic.IsTruncated(text), Is.True);
    }

    [Test]
    public void IsTruncated_TextThatFitsAt45CharsPerLineButNotAt40_IsTruncated()
    {
        // The bio renders at Body2 (15sp), not the 14sp the old constant assumed, so fewer
        // characters fit per line than 45. #138's rule: where the estimate has to be wrong, it
        // should show a redundant "Read more" rather than silently tail-truncate the text away.
        var text = new string('a', 340);

        Assert.That(DescriptionTruncationHeuristic.IsTruncated(text), Is.True);
    }

    [Test]
    public void IsTruncated_ManyLineBreaks_True()
        // Once the markdown processor emits <br> for character and staff bios (#138), this is the
        // branch that catches a short-but-tall bio.
        => Assert.That(DescriptionTruncationHeuristic.IsTruncated("One.<br>Two.<br>Three.<br>Four."), Is.True);

    [Test]
    public void IsTruncated_HtmlTagsDontCountTowardLimit()
    {
        // Lots of tag markup but few visible chars + no break tags = not truncated.
        var text = "<b>hi</b><i>there</i><u>!</u>";
        Assert.That(DescriptionTruncationHeuristic.IsTruncated(text), Is.False);
    }

    [Test]
    public void IsTruncated_ManyParagraphBreaks_True()
    {
        // 3+ paragraph breaks even with short total chars = visually long.
        var text = "<p>One.</p><p>Two.</p><p>Three.</p>";
        Assert.That(DescriptionTruncationHeuristic.IsTruncated(text), Is.True);
    }

    [Test]
    public void IsTruncated_EntityDecode_DoesntInflateCount()
    {
        // &amp; is 5 chars in source, 1 char visible. Make sure we count visible.
        var text = string.Concat(Enumerable.Repeat("&amp;", 100));
        // 100 visible chars after decode; well below the 360 threshold and no breaks.
        Assert.That(DescriptionTruncationHeuristic.IsTruncated(text), Is.False);
    }
}
