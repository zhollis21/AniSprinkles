namespace AniSprinkles.UnitTests;

public class QueryAttributeParserTests
{
    [Test]
    public void ParseInt_IntValue_ReturnsIt()
    {
        var query = new Dictionary<string, object> { ["studioId"] = 18 };
        Assert.That(QueryAttributeParser.ParseInt(query, "studioId"), Is.EqualTo(18));
    }

    [Test]
    public void ParseInt_NumericString_Parses()
    {
        var query = new Dictionary<string, object> { ["mediaId"] = "1535" };
        Assert.That(QueryAttributeParser.ParseInt(query, "mediaId"), Is.EqualTo(1535));
    }

    [Test]
    public void ParseInt_MissingKey_ReturnsZero()
    {
        var query = new Dictionary<string, object> { ["other"] = 5 };
        Assert.That(QueryAttributeParser.ParseInt(query, "studioId"), Is.EqualTo(0));
    }

    [Test]
    public void ParseInt_NonNumericString_ReturnsZero()
    {
        var query = new Dictionary<string, object> { ["studioId"] = "abc" };
        Assert.That(QueryAttributeParser.ParseInt(query, "studioId"), Is.EqualTo(0));
    }

    [Test]
    public void ParseInt_UnexpectedType_ReturnsZero()
    {
        var query = new Dictionary<string, object> { ["studioId"] = 3.5 };
        Assert.That(QueryAttributeParser.ParseInt(query, "studioId"), Is.EqualTo(0));
    }
}
