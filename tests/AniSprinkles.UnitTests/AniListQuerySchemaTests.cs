using System.Reflection;
using AniSprinkles.Services;

namespace AniSprinkles.UnitTests;

/// <summary>
/// Guards the GraphQL documents in <see cref="AniListClient"/> against schema changes we have already
/// migrated past (#163), in the same spirit as <see cref="IconGlyphReferenceTests"/>: the thing being
/// prevented is not a build error.
/// <para>
/// Both migrations here are field-level and backwards compatible for now — AniList still answers the
/// deprecated <c>averageScore</c>, and <c>relationType(version: 2)</c> still resolves — so reverting
/// one breaks nothing visibly. The app renders, the recorded fixtures replay, and the only symptom is
/// a score that stops being maintained or a relation that silently reports <c>OTHER</c> instead of
/// <c>SAME_UNIVERSE</c>.
/// </para>
/// <para>
/// Asserted across every query constant rather than per operation, because the fields appear in
/// fourteen places across twelve documents and a new query is far more likely to be written by
/// copying an existing one than by consulting the schema.
/// </para>
/// </summary>
public class AniListQuerySchemaTests
{
    [Fact]
    public void NoQueryAsksForTheDeprecatedAverageScore()
    {
        var offenders = GraphQlDocuments()
            .Where(d => d.Text.Contains("averageScore", StringComparison.Ordinal))
            .Select(d => d.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheScoreFieldIsStillSelectedSomewhere()
    {
        // The counterpart to the assertion above: that one also passes if the field is dropped
        // entirely, which would blank every score in the app without failing anything.
        //
        // A floor rather than an exact count, deliberately. Twelve documents select the field in
        // their own text, but the four constants that append BrowseMediaFragment are themselves
        // const expressions, so the fragment's text is concatenated at compile time and they
        // contain it too — the real total is sixteen and moves whenever a query is added or starts
        // reusing the fragment. Pinning that number would fail on unrelated work while catching
        // nothing this does not.
        var selecting = GraphQlDocuments()
            .Count(d => d.Text.Contains("weightedScore", StringComparison.Ordinal));

        Assert.True(selecting >= 12, $"Only {selecting} documents select weightedScore.");
    }

    [Fact]
    public void RelationsAreRequestedAtVersion3()
    {
        var documents = GraphQlDocuments().ToList();

        Assert.DoesNotContain(documents, d => d.Text.Contains("relationType(version: 2)", StringComparison.Ordinal));
        Assert.Contains(documents, d => d.Text.Contains("relationType(version: 3)", StringComparison.Ordinal));
    }

    /// <summary>
    /// The query constants are private, so they are read by reflection over the literals rather than
    /// by exercising every operation through the client — twelve scripted responses to assert one
    /// field name each would be far more code guarding the same thing.
    /// </summary>
    private static IEnumerable<(string Name, string Text)> GraphQlDocuments()
    {
        var literals = typeof(AniListClient)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Text: (string?)f.GetRawConstantValue() ?? string.Empty))
            .Where(d => d.Text.Contains('{') && (
                d.Text.Contains("query ", StringComparison.Ordinal)
                || d.Text.Contains("mutation ", StringComparison.Ordinal)
                || d.Text.Contains("fragment ", StringComparison.Ordinal)))
            .ToList();

        // A vacuous pass is the failure mode that matters: if the constants are ever renamed, made
        // non-literal, or moved to another type, every assertion above would quietly hold over an
        // empty set. Twelve is the count that selects the score field, so the floor sits just below
        // the real total rather than at an arbitrary number.
        Assert.True(
            literals.Count >= 15,
            $"Expected to find the GraphQL documents by reflection, found {literals.Count}. "
            + "If the query constants moved, this helper needs updating — the assertions are "
            + "meaningless until it finds them again.");

        return literals;
    }
}
