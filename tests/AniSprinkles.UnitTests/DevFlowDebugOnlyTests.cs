using System.Xml.Linq;

namespace AniSprinkles.UnitTests;

// The MAUI DevFlow agent (#170) serves the visual tree, app storage and captured network traffic —
// AniList's Authorization header included — to anything that can reach it. It must never ship.
// The guarantee is the package reference's Debug-only condition: without the assembly in the APK
// there is nothing to start. MauiProgram's #if DEBUG is not the guarantee, because a Release build
// would still compile with the reference unconditional, and the CI Release launch would not notice
// an extra agent. So the condition is pinned here, across every MSBuild file in the repo.
public class DevFlowDebugOnlyTests
{
    private const string DebugOnlyCondition = "'$(Configuration)' == 'Debug'";

    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    [Test]
    public void DevFlowPackageReferences_AreFound()
    {
        // Without this, every assertion below could pass vacuously after a rename or a move.
        Assert.That(DevFlowPackageReferences(), Is.Not.Empty);
    }

    [Test]
    public void EveryDevFlowPackageReference_IsDebugOnly()
    {
        var offenders = DevFlowPackageReferences()
            .Where(r => r.Conditions.All(c => Normalize(c) != Normalize(DebugOnlyCondition)))
            .Select(r => $"{r.File}: {r.Include} (conditions: {(r.Conditions.Count == 0 ? "none" : string.Join(" | ", r.Conditions))})")
            .ToList();

        Assert.That(offenders.Count == 0,
            Is.True,
            "MAUI DevFlow must be referenced only under Condition=\"" + DebugOnlyCondition + "\":\n"
            + string.Join("\n", offenders));
    }

    [TestCase(new string[0], false)]
    [TestCase(new[] { "'$(Configuration)' == 'Debug'" }, true)]
    [TestCase(new[] { " '$(Configuration)'=='Debug' " }, true)]
    [TestCase(new[] { "'$(CiBuild)' == 'true'" }, false)]
    [TestCase(new[] { "'$(Configuration)' != 'Release'" }, false)]
    [TestCase(new[] { "'$(Configuration)' == 'Debug' OR '$(CiBuild)' == 'true'" }, false)]
    public void ConditionCheck_AcceptsOnlyThePlainDebugCondition(string[] conditions, bool accepted)
    {
        // The rule is deliberately exact: anything that is not literally "Debug" — an OR, a
        // != Release, a CI-only flag — can admit a configuration that ships.
        var isAccepted = conditions.Any(c => Normalize(c) == Normalize(DebugOnlyCondition));
        Assert.That(isAccepted, Is.EqualTo(accepted));
    }

    private static List<DevFlowReference> DevFlowPackageReferences()
    {
        var root = RepoRoot.Value;
        var results = new List<DevFlowReference>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (ext is not (".csproj" or ".props" or ".targets"))
            {
                continue;
            }

            // .claude/ holds other git worktrees when this runs from the main checkout; their
            // project files are other branches' business.
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.StartsWith(".claude/", StringComparison.Ordinal))
            {
                continue;
            }

            var doc = XDocument.Load(file);
            foreach (var reference in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            {
                var include = (string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update") ?? string.Empty;
                if (!include.StartsWith("Microsoft.Maui.DevFlow", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // A condition can sit on the reference itself or on any enclosing group.
                var conditions = reference.AncestorsAndSelf()
                    .Select(e => (string?)e.Attribute("Condition"))
                    .OfType<string>()
                    .ToList();
                results.Add(new DevFlowReference(rel, include, conditions));
            }
        }

        return results;
    }

    private static string Normalize(string condition) =>
        string.Concat(condition.Where(c => !char.IsWhiteSpace(c)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AniSprinkles.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root (AniSprinkles.slnx) walking up from {AppContext.BaseDirectory}");
        }

        return dir.FullName;
    }

    private sealed record DevFlowReference(string File, string Include, List<string> Conditions);
}
