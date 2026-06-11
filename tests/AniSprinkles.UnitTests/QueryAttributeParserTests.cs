namespace AniSprinkles.UnitTests;

public class QueryAttributeParserTests
{
    [Fact]
    public void ParseInt_IntValue_ReturnsIt()
    {
        var query = new Dictionary<string, object> { ["studioId"] = 18 };
        Assert.Equal(18, QueryAttributeParser.ParseInt(query, "studioId"));
    }

    [Fact]
    public void ParseInt_NumericString_Parses()
    {
        var query = new Dictionary<string, object> { ["mediaId"] = "1535" };
        Assert.Equal(1535, QueryAttributeParser.ParseInt(query, "mediaId"));
    }

    [Fact]
    public void ParseInt_MissingKey_ReturnsZero()
    {
        var query = new Dictionary<string, object> { ["other"] = 5 };
        Assert.Equal(0, QueryAttributeParser.ParseInt(query, "studioId"));
    }

    [Fact]
    public void ParseInt_NonNumericString_ReturnsZero()
    {
        var query = new Dictionary<string, object> { ["studioId"] = "abc" };
        Assert.Equal(0, QueryAttributeParser.ParseInt(query, "studioId"));
    }

    [Fact]
    public void ParseInt_UnexpectedType_ReturnsZero()
    {
        var query = new Dictionary<string, object> { ["studioId"] = 3.5 };
        Assert.Equal(0, QueryAttributeParser.ParseInt(query, "studioId"));
    }
}
