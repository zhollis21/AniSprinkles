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
    [Test]
    public void NoQueryAsksForTheDeprecatedAverageScore()
    {
        var offenders = GraphQlDocuments()
            .Where(d => d.Text.Contains("averageScore", StringComparison.Ordinal))
            .Select(d => d.Name)
            .ToList();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
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

        Assert.That(selecting >= 12, Is.True, $"Only {selecting} documents select weightedScore.");
    }

    [Test]
    public void RelationsAreRequestedAtVersion3()
    {
        var documents = GraphQlDocuments().ToList();

        Assert.That(documents, Has.None.Matches<(string Name, string Text)>(d => d.Text.Contains("relationType(version: 2)", StringComparison.Ordinal)));
        Assert.That(documents, Has.Some.Matches<(string Name, string Text)>(d => d.Text.Contains("relationType(version: 3)", StringComparison.Ordinal)));
    }

    /// <summary>
    /// #199. AniList returns 18+ titles in every nested media connection, signed in or not, and none
    /// of them take an <c>isAdult</c> argument, so the details pages filter on the flag themselves.
    /// A media selection that leaves it out deserializes as <c>IsAdult = null</c>, which the filter
    /// reads as "not adult": the leak reopens with nothing failing.
    /// <para>
    /// The one exemption is the <c>browseMedia</c> fragment. Search, Browse and Discover filter
    /// server-side with the <c>isAdult</c> argument, so the flag on their results is never consulted.
    /// Mutations are exempt too: they echo back an entry the viewer just saved.
    /// </para>
    /// </summary>
    [Test]
    public void EveryMediaSelectionInAQuery_AsksForTheAdultFlag()
    {
        var offenders = new List<string>();
        foreach (var (name, text) in GraphQlDocuments().Where(d => d.Text.Contains("query ", StringComparison.Ordinal)))
        {
            foreach (var block in MediaSelections(text))
            {
                if (!block.Contains("isAdult", StringComparison.Ordinal))
                {
                    offenders.Add($"{name}: {block[..Math.Min(block.Length, 60)].ReplaceLineEndings(" ")}");
                }
            }
        }

        Assert.That(offenders, Is.Empty);
    }

    /// <summary>
    /// The body of every selection set that picks a media title, at its own depth only (a nested
    /// <c>mediaListEntry { ... }</c> does not count). The <c>browseMedia</c> fragment is skipped.
    /// </summary>
    private static IEnumerable<string> MediaSelections(string document)
    {
        const string Marker = "title { romaji english native }";
        for (var at = document.IndexOf(Marker, StringComparison.Ordinal); at >= 0;
             at = document.IndexOf(Marker, at + Marker.Length, StringComparison.Ordinal))
        {
            var open = OpeningBrace(document, at);
            var close = ClosingBrace(document, open);
            var header = document[Math.Max(0, open - 40)..open];
            if (header.Contains("fragment browseMedia", StringComparison.Ordinal))
            {
                continue;
            }

            yield return TopLevelOnly(document[(open + 1)..close]);
        }
    }

    private static int OpeningBrace(string text, int from)
    {
        var depth = 0;
        for (var i = from; i >= 0; i--)
        {
            if (text[i] == '}')
            {
                depth++;
            }
            else if (text[i] == '{' && depth-- == 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Unbalanced braces in a GraphQL document.");
    }

    private static int ClosingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Unbalanced braces in a GraphQL document.");
    }

    private static string TopLevelOnly(string body)
    {
        var kept = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var c in body)
        {
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
            else if (depth == 0)
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
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
        Assert.That(
            literals.Count >= 15,
            Is.True,
            $"Expected to find the GraphQL documents by reflection, found {literals.Count}. "
            + "If the query constants moved, this helper needs updating — the assertions are "
            + "meaningless until it finds them again.");

        return literals;
    }
}
