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
        // 8 lines * 45 chars = 360. Beyond that should trip the visible-char rule.
        var text = new string('a', 500);
        Assert.True(DescriptionTruncationHeuristic.IsTruncated(text));
    }

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
