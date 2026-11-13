using AniSprinkles.PageModels;

namespace AniSprinkles.UnitTests;

// Build-time guard for the Library sort picker: the picker only ever emits these codes, and SelectSort
// treats any malformed code as a logged no-op. These tests assert every definition's Code parses by the
// exact same rules SelectSort applies, so a typo'd entry (or a renamed SortField) fails CI before it ships.
public class MediaListSortDefinitionsTests
{
    public static IEnumerable<string> AllCodes()
        => MediaListSortDefinitions.All.Select(d => d.Code);

    [TestCaseSource(nameof(AllCodes))]
    public void Code_ParsesAsFieldAndDirection(string code)
    {
        var parts = code.Split(':');

        Assert.That(parts.Length, Is.EqualTo(2));
        Assert.That(Enum.TryParse<SortField>(parts[0], out _), Is.True, $"'{parts[0]}' is not a SortField");
        Assert.That(new[] { "asc", "desc" }, Does.Contain(parts[1]));
    }

    [Test]
    public void Codes_AreUnique()
    {
        var codes = MediaListSortDefinitions.All.Select(d => d.Code).ToList();

        Assert.That(codes.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(codes.Count));
    }

    [Test]
    public void Definitions_AllHaveDisplayText()
        => Assert.Multiple(() =>
        {
            foreach (var d in MediaListSortDefinitions.All)
            {
                Assert.That(string.IsNullOrWhiteSpace(d.Display), Is.False, d.Code);
            }
        });
}
