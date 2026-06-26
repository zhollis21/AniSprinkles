using AniSprinkles.PageModels;

namespace AniSprinkles.UnitTests;

// Build-time guard for the My Anime sort picker: the picker only ever emits these codes, and SelectSort
// treats any malformed code as a logged no-op. These tests assert every definition's Code parses by the
// exact same rules SelectSort applies, so a typo'd entry (or a renamed SortField) fails CI before it ships.
public class MyAnimeSortDefinitionsTests
{
    public static TheoryData<string> AllCodes()
    {
        var data = new TheoryData<string>();
        foreach (var (code, _) in MyAnimeSortDefinitions.All)
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Code_ParsesAsFieldAndDirection(string code)
    {
        var parts = code.Split(':');

        Assert.Equal(2, parts.Length);
        Assert.True(Enum.TryParse<SortField>(parts[0], out _), $"'{parts[0]}' is not a SortField");
        Assert.Contains(parts[1], new[] { "asc", "desc" });
    }

    [Fact]
    public void Codes_AreUnique()
    {
        var codes = MyAnimeSortDefinitions.All.Select(d => d.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Definitions_AllHaveDisplayText()
        => Assert.All(MyAnimeSortDefinitions.All, d => Assert.False(string.IsNullOrWhiteSpace(d.Display)));
}
