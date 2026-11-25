using System.Text.Json.Nodes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The recorded list and the recorded details pages describe one account, so they must agree on each
/// title's entry. Recording is resumable, which lets one be re-recorded after the account has moved on
/// and not the other: #233 did that for the details pages, and a title then opened as Completed from a
/// Watching card, in the build the journeys and screenshots come from.
/// </summary>
public class FixtureListConsistencyTests
{
    private static readonly string[] EntryFields = ["status", "progress", "progressVolumes", "score", "repeat"];

    [Test]
    public void DetailsFixtures_AgreeWithTheListFixtures_AboutTheViewersEntry()
    {
        var listed = ReadFixtures("MediaListCollection__*.json")
            .SelectMany(data => data["MediaListCollection"]?["lists"]?.AsArray() ?? [])
            .SelectMany(list => list?["entries"]?.AsArray() ?? [])
            .OfType<JsonNode>()
            .ToLookup(entry => entry["mediaId"]?.GetValue<int>() ?? 0);

        var compared = 0;
        var disagreements = new List<string>();
        foreach (var media in ReadFixtures("Media__*.json").Select(data => data["Media"]).OfType<JsonNode>())
        {
            var id = media["id"]?.GetValue<int>() ?? 0;
            foreach (var entry in listed[id])
            {
                compared++;
                var details = media["mediaListEntry"];
                disagreements.AddRange(EntryFields
                    .Where(field => !JsonNode.DeepEquals(entry[field], details?[field]))
                    .Select(field => $"media {id}: {field} is {entry[field]} on the list and {details?[field]} on its details page"));
            }
        }

        Assert.That(compared, Is.GreaterThan(10), "Too few titles compared; the fixtures moved or changed shape.");
        Assert.That(disagreements, Is.Empty,
            "The test account changed between recordings. Put it back or re-record both (tools/record-anilist-fixtures.cs):"
            + Environment.NewLine + string.Join(Environment.NewLine, disagreements));
    }

    private static List<JsonNode> ReadFixtures(string pattern)
        => Directory.EnumerateFiles(
                Path.Combine(XamlFileValidationTests.RepoRoot.Value, "src", "AniSprinkles", "Fixtures", "AniList"), pattern)
            .Select(file => JsonNode.Parse(File.ReadAllText(file))?["response"]?["data"])
            .OfType<JsonNode>()
            .ToList();
}
