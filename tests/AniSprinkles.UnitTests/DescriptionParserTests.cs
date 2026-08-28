namespace AniSprinkles.UnitTests;

public class DescriptionParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        var result = DescriptionParser.Parse(null);
        Assert.Empty(result.Stats);
        Assert.Equal(string.Empty, result.Prose);
    }

    [Fact]
    public void Parse_PureProse_ReturnsAsProse()
    {
        var result = DescriptionParser.Parse("She was born in a dairy farm.\nShe became a mangaka.");
        Assert.Empty(result.Stats);
        Assert.Contains("dairy farm", result.Prose);
    }

    [Fact]
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

        Assert.Equal(5, result.Stats.Count);

        Assert.Equal("Height", result.Stats[0].Label);
        Assert.Equal("172 cm", result.Stats[0].Value);
        Assert.False(result.Stats[0].IsRowSpoiler);
        Assert.False(result.Stats[0].IsValueSpoiler);

        Assert.Equal("Affiliations", result.Stats[1].Label);
        Assert.Equal("Straw Hat Pirates (Captain); Four Emperors", result.Stats[1].Value);
        Assert.False(result.Stats[1].IsRowSpoiler);
        Assert.True(result.Stats[1].IsValueSpoiler);

        Assert.Equal("Devil Fruit", result.Stats[2].Label);

        Assert.Equal("True Devil Fruit", result.Stats[3].Label);
        Assert.Equal("Hito Hito no Mi Model: Nika", result.Stats[3].Value);
        Assert.True(result.Stats[3].IsRowSpoiler);

        Assert.Equal("Bounty", result.Stats[4].Label);
        Assert.True(result.Stats[4].IsValueSpoiler);

        Assert.Contains("Luffy is the captain", result.Prose);
    }

    [Fact]
    public void Parse_BlankLineEndsStats_ProseAfterIsCollected()
    {
        var input = "__Height:__ 172 cm\n\nThe rest is prose.\n__This:__ is not a stat row anymore.";
        var result = DescriptionParser.Parse(input);

        Assert.Single(result.Stats);
        Assert.Equal("Height", result.Stats[0].Label);
        // Once prose starts, subsequent stat-shaped lines stay in prose to avoid splitting context.
        Assert.Contains("The rest is prose", result.Prose);
        Assert.Contains("__This:__", result.Prose);
    }

    [Fact]
    public void Parse_NonStatLineFirst_AllGoesToProse()
    {
        var input = "Just some prose.\n__Trailing:__ stat that won't be extracted.";
        var result = DescriptionParser.Parse(input);
        Assert.Empty(result.Stats);
        Assert.Contains("Just some prose", result.Prose);
        Assert.Contains("__Trailing:__", result.Prose);
    }

    [Fact]
    public void Parse_LabelWithSpaces_IsAccepted()
    {
        var result = DescriptionParser.Parse("__Devil Fruit Type:__ Paramecia");
        Assert.Single(result.Stats);
        Assert.Equal("Devil Fruit Type", result.Stats[0].Label);
    }

    [Fact]
    public void Parse_EmptyLabel_DropsTheLine()
    {
        var result = DescriptionParser.Parse("__:__ value");
        Assert.Empty(result.Stats);
        Assert.Contains("__:__ value", result.Prose);
    }

    [Fact]
    public void Parse_BoldOnlyNoColon_IsProse()
    {
        // __Some bold text__ without a colon shouldn't be parsed as a stat.
        var result = DescriptionParser.Parse("__Some bold text__\nNot a stat row.");
        Assert.Empty(result.Stats);
        Assert.Contains("__Some bold text__", result.Prose);
    }

    [Fact]
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

        Assert.Equal(2, result.Stats.Count);
        Assert.Equal("Height", result.Stats[0].Label);
        Assert.Equal("145-180 cm", result.Stats[0].Value);
        Assert.Equal("Family", result.Stats[1].Label);
        Assert.True(result.Stats[1].IsValueSpoiler);
        Assert.Equal("Born in Konohagakure.", result.Prose);
    }

    [Fact]
    public void Parse_BoldRunWithNoColonEitherSideOfTheUnderscores_IsStillProse()
        // The guard on the above: widening the pattern must not start treating an ordinary bold
        // run as a stat row.
        => Assert.Empty(DescriptionParser.Parse("__Some bold text__\nNot a stat row.").Stats);

    [Fact]
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

        Assert.Equal(4, result.Stats.Count);

        Assert.Equal("True Devil Fruit", result.Stats[1].Label);
        Assert.Equal("Hito Hito no Mi Model: Nika", result.Stats[1].Value);
        Assert.True(result.Stats[1].IsRowSpoiler);

        Assert.Equal("True Devil Fruit Type", result.Stats[2].Label);
        Assert.Equal("Mythical Zoan", result.Stats[2].Value);
        Assert.True(result.Stats[2].IsRowSpoiler);

        // The block closed, so the row after it is read normally rather than as part of it.
        Assert.Equal("Bounty", result.Stats[3].Label);
        Assert.False(result.Stats[3].IsRowSpoiler);
        Assert.True(result.Stats[3].IsValueSpoiler);

        Assert.Equal("Luffy is the captain of the Straw Hat Pirates.", result.Prose);
        Assert.DoesNotContain("True Devil Fruit", result.Prose);
    }

    [Fact]
    public void Parse_SpoilerBlockThatNeverCloses_StillKeepsItsRowsAsSpoilers()
    {
        // Defensive: an unterminated block shouldn't spill the rest of the stats into prose either.
        var input = "__A:__ one\n~!__B:__ two\n__C:__ three";

        var result = DescriptionParser.Parse(input);

        Assert.Equal(3, result.Stats.Count);
        Assert.False(result.Stats[0].IsRowSpoiler);
        Assert.True(result.Stats[1].IsRowSpoiler);
        Assert.True(result.Stats[2].IsRowSpoiler);
    }
}

public class DescriptionTruncationHeuristicTests
{
    [Fact]
    public void IsTruncated_NullOrEmpty_False()
    {
        Assert.False(DescriptionTruncationHeuristic.IsTruncated(null));
        Assert.False(DescriptionTruncationHeuristic.IsTruncated(""));
        Assert.False(DescriptionTruncationHeuristic.IsTruncated("   "));
    }

    [Fact]
    public void IsTruncated_ShortText_False()
    {
        Assert.False(DescriptionTruncationHeuristic.IsTruncated("Just one short sentence."));
    }

    [Fact]
    public void IsTruncated_VisibleCharOverflow_True()
    {
        // 8 lines * 40 chars = 320. Beyond that should trip the visible-char rule.
        var text = new string('a', 500);
        Assert.True(DescriptionTruncationHeuristic.IsTruncated(text));
    }

    [Fact]
    public void IsTruncated_TextThatFitsAt45CharsPerLineButNotAt40_IsTruncated()
    {
        // The bio renders at Body2 (15sp), not the 14sp the old constant assumed, so fewer
        // characters fit per line than 45. #138's rule: where the estimate has to be wrong, it
        // should show a redundant "Read more" rather than silently tail-truncate the text away.
        var text = new string('a', 340);

        Assert.True(DescriptionTruncationHeuristic.IsTruncated(text));
    }

    [Fact]
    public void IsTruncated_ManyLineBreaks_True()
        // Once the markdown processor emits <br> for character and staff bios (#138), this is the
        // branch that catches a short-but-tall bio.
        => Assert.True(DescriptionTruncationHeuristic.IsTruncated("One.<br>Two.<br>Three.<br>Four."));

    [Fact]
    public void IsTruncated_HtmlTagsDontCountTowardLimit()
    {
        // Lots of tag markup but few visible chars + no break tags = not truncated.
        var text = "<b>hi</b><i>there</i><u>!</u>";
        Assert.False(DescriptionTruncationHeuristic.IsTruncated(text));
    }

    [Fact]
    public void IsTruncated_ManyParagraphBreaks_True()
    {
        // 3+ paragraph breaks even with short total chars = visually long.
        var text = "<p>One.</p><p>Two.</p><p>Three.</p>";
        Assert.True(DescriptionTruncationHeuristic.IsTruncated(text));
    }

    [Fact]
    public void IsTruncated_EntityDecode_DoesntInflateCount()
    {
        // &amp; is 5 chars in source, 1 char visible. Make sure we count visible.
        var text = string.Concat(Enumerable.Repeat("&amp;", 100));
        // 100 visible chars after decode; well below the 360 threshold and no breaks.
        Assert.False(DescriptionTruncationHeuristic.IsTruncated(text));
    }
}
