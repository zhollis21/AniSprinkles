using System.Xml.Linq;

namespace AniSprinkles.UnitTests;

// Guards against structural corruption in .xaml files that can slip past the MAUI build
// (e.g., a botched edit that splits an attribute value across other elements). XamlC
// reports semantic errors but has historically missed some well-formedness failures
// in ResourceDictionary files, so we parse every .xaml as plain XML here.
public class XamlFileValidationTests
{
    // Cached once per test run — FindRepoRoot walks parent directories and would otherwise
    // be called once per file during discovery and once per file during validation.
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    [Theory]
    [MemberData(nameof(AllXamlFiles))]
    public void XamlFile_IsWellFormedXml(string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot.Value, relativePath);
        var ex = Record.Exception(() => XDocument.Load(fullPath, LoadOptions.SetLineInfo));
        Assert.True(ex is null, $"{relativePath} - {ex?.Message}");
    }

    [Fact]
    public void Discovery_FindsXamlFiles()
    {
        Assert.NotEmpty(AllXamlFiles());
    }

    public static IEnumerable<object[]> AllXamlFiles()
    {
        var repoRoot = RepoRoot.Value;
        var srcRoot = Path.Combine(repoRoot, "src");
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/"))
            {
                continue;
            }

            yield return new object[] { rel };
        }
    }

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
}
