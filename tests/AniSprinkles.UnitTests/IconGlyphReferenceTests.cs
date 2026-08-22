using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using AniSprinkles.Icons;

namespace AniSprinkles.UnitTests;

/// <summary>
/// Guards every Fluent icon glyph reference in the repo against the actual icon assembly (issue #117).
///
/// Two things can go wrong, and neither is a build error:
///
/// 1. <c>x:Static</c> in XAML is resolved by the XAML loader at runtime, so a misspelled glyph builds
///    clean and throws <c>XamlParseException</c> the moment the page loads. <c>AppShell.xaml</c> shipped
///    <c>FluentIconsRegular.Compass24</c> (the real member is <c>CompassNorthwest24</c>) and crashed on
///    launch; the same typo on a details page would have built, tested and screenshotted green.
/// 2. <see cref="Glyphs"/> mirrors glyph constants into AniSprinkles.Core, which cannot reference the
///    platform-only icon package (#62). A mirror can drift from its source.
///
/// The assembly is read through PE metadata rather than <c>Assembly.Load</c>: it targets
/// <c>net10.0-android36.0</c> and will not load into this <c>net10.0</c> test host.
/// </summary>
public class IconGlyphReferenceTests
{
    private const string PackageId = "IconFont.Maui.FluentIcons";
    private const string IconNamespace = "IconFont.Maui.FluentIcons";

    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    // Reading the PE metadata is cheap but not free, and MemberData runs discovery separately from
    // execution — parse the assembly once for the whole run.
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>?> IconTable =
        new(LoadIconTable);

    [Theory]
    [MemberData(nameof(XamlIconReferences))]
    public void XamlIconReference_ResolvesInIconAssembly(string relativePath, string typeName, string memberName)
    {
        var table = RequireIconTable();

        Assert.True(
            table.TryGetValue(typeName, out var members),
            $"{relativePath}: '{typeName}' is not a type in {PackageId}.");

        Assert.True(
            members.ContainsKey(memberName),
            $"{relativePath}: '{typeName}.{memberName}' does not exist in {PackageId}. " +
            $"{SuggestionFor(members.Keys, memberName)}");
    }

    [Theory]
    [MemberData(nameof(MirroredGlyphConstants))]
    public void MirroredGlyph_MatchesPackageNameAndValue(string sourceTypeName, string memberName, string mirroredValue)
    {
        var table = RequireIconTable();

        Assert.True(
            table.TryGetValue(sourceTypeName, out var members),
            $"Glyphs mirrors '{sourceTypeName}', which is not a type in {PackageId}.");

        Assert.True(
            members.TryGetValue(memberName, out var packageValue),
            $"Glyphs mirrors '{sourceTypeName}.{memberName}', which no longer exists in {PackageId}. " +
            $"{SuggestionFor(members.Keys, memberName)}");

        Assert.True(
            packageValue == mirroredValue,
            $"Glyphs.{Nested(sourceTypeName)}.{memberName} is {Describe(mirroredValue)} but " +
            $"{sourceTypeName}.{memberName} is {Describe(packageValue)}. " +
            "Regenerate with: dotnet run tools/generate-glyphs.cs");
    }

    // A silently-empty MemberData would make both theories above vacuously green — exactly the
    // failure mode #117 exists to prevent.
    [Fact]
    public void Discovery_FindsXamlIconReferences() => Assert.NotEmpty(XamlIconReferences());

    [Fact]
    public void Discovery_FindsMirroredGlyphConstants() => Assert.NotEmpty(MirroredGlyphConstants());

    public static TheoryData<string, string, string> XamlIconReferences()
    {
        var data = new TheoryData<string, string, string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in EnumerateXamlFiles())
        {
            var text = File.ReadAllText(file);
            var relativePath = Path.GetRelativePath(RepoRoot.Value, file).Replace('\\', '/');

            // Resolve the prefix from the xmlns declaration rather than assuming "icons:", so renaming
            // it cannot quietly switch this test off.
            var prefixes = Regex
                .Matches(text, $@"xmlns:(?<prefix>[\w.]+)\s*=\s*""clr-namespace:{Regex.Escape(IconNamespace)}\b")
                .Select(m => m.Groups["prefix"].Value)
                .ToHashSet(StringComparer.Ordinal);

            if (prefixes.Count == 0)
            {
                continue;
            }

            foreach (Match match in Regex.Matches(
                text,
                @"x:Static\s+(?<prefix>[\w.]+):(?<type>\w+)\.(?<member>\w+)"))
            {
                if (!prefixes.Contains(match.Groups["prefix"].Value))
                {
                    continue;
                }

                var typeName = match.Groups["type"].Value;
                var memberName = match.Groups["member"].Value;

                // The same glyph is referenced from many files; one case per (file, member) is enough.
                if (seen.Add($"{relativePath}|{typeName}.{memberName}"))
                {
                    data.Add(relativePath, typeName, memberName);
                }
            }
        }

        return data;
    }

    public static TheoryData<string, string, string> MirroredGlyphConstants()
    {
        var data = new TheoryData<string, string, string>();

        foreach (var nested in typeof(Glyphs).GetNestedTypes(BindingFlags.Public))
        {
            // Glyphs.Regular mirrors FluentIconsRegular, Glyphs.Filled mirrors FluentIconsFilled.
            var sourceTypeName = $"FluentIcons{nested.Name}";

            foreach (var field in nested.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field is { IsLiteral: true, IsInitOnly: false } && field.GetRawConstantValue() is string value)
                {
                    data.Add(sourceTypeName, field.Name, value);
                }
            }
        }

        return data;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RequireIconTable()
    {
        // A missing package cache (a fresh machine, a version bump that has not been restored) should
        // not turn into a red build for something this test cannot check.
        Assert.SkipWhen(
            IconTable.Value is null,
            $"{PackageId} was not found in the NuGet package cache; skipping glyph validation.");

        return IconTable.Value!;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? LoadIconTable()
    {
        var assemblyPath = ResolveIconAssembly();
        if (assemblyPath is null)
        {
            return null;
        }

        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            if (!md.GetString(type.Namespace).Equals(IconNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fieldHandle in type.GetFields())
            {
                var field = md.GetFieldDefinition(fieldHandle);
                if (!field.Attributes.HasFlag(FieldAttributes.Literal))
                {
                    continue;
                }

                var constantHandle = field.GetDefaultValue();
                if (constantHandle.IsNil)
                {
                    continue;
                }

                var constant = md.GetConstant(constantHandle);
                if (constant.TypeCode != ConstantTypeCode.String)
                {
                    continue;
                }

                // Read the constant blob rather than pattern-matching the metadata #Strings heap:
                // heap entries share NUL delimiters, so a regex over it silently drops every other
                // name. Substring-matching the raw file is worse still — it reports Compass24 as
                // present because CompassNorthwest24 contains it.
                var blob = md.GetBlobReader(constant.Value);
                members[md.GetString(field.Name)] = blob.ReadUTF16(blob.Length);
            }

            if (members.Count > 0)
            {
                result[md.GetString(type.Name)] = members;
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static string? ResolveIconAssembly()
    {
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");

        var libRoot = Path.Combine(packagesRoot, PackageId.ToLowerInvariant(), ResolvePackageVersion(), "lib");
        if (!Directory.Exists(libRoot))
        {
            // Genuinely environmental — a machine that has not restored, or a version bump not yet
            // pulled. This is the only condition the theories are allowed to skip on.
            return null;
        }

        // Any platform TFM will do — the glyph literals are identical across them.
        return Directory
            .EnumerateFiles(libRoot, $"{PackageId}.dll", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Reads the pinned version out of whichever project declares the package, so a bump points this
    /// test at the new assembly instead of silently validating against a stale one.
    /// <para>
    /// Scans every csproj under <c>src/</c> rather than naming one: hardcoding the app's path meant
    /// that moving the project (#62's sibling-layout restructure) turned all 190 glyph cases into
    /// silent skips, which is precisely the hole #117 exists to close. A missing declaration is a
    /// repo-structure problem, not an environment gap, so it throws instead of skipping.
    /// </para>
    /// </summary>
    private static string ResolvePackageVersion()
    {
        var pattern = $@"PackageReference\s+Include=""{Regex.Escape(PackageId)}""\s+Version=""(?<version>[^""]+)""";

        foreach (var csproj in Directory.EnumerateFiles(
            Path.Combine(RepoRoot.Value, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var match = Regex.Match(File.ReadAllText(csproj), pattern);
            if (match.Success)
            {
                return match.Groups["version"].Value;
            }
        }

        throw new InvalidOperationException(
            $"No project under src/ declares a PackageReference to {PackageId}. If the package was " +
            "removed on purpose, delete Core's mirrored Glyphs.cs and this test together; otherwise " +
            "this test can no longer validate anything.");
    }

    private static IEnumerable<string> EnumerateXamlFiles()
    {
        var srcRoot = Path.Combine(RepoRoot.Value, "src");
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(RepoRoot.Value, file).Replace('\\', '/');
            if (!rel.Contains("/bin/") && !rel.Contains("/obj/"))
            {
                yield return file;
            }
        }
    }

    /// <summary>Points at the likely intended member — the Compass24/CompassNorthwest24 case.</summary>
    private static string SuggestionFor(IEnumerable<string> candidates, string memberName)
    {
        var trimmed = memberName.TrimEnd("0123456789".ToCharArray());
        var near = candidates
            .Where(c => c.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Length)
            .Take(4)
            .ToList();

        return near.Count > 0 ? $"Did you mean: {string.Join(", ", near)}?" : "No similarly-named member exists.";
    }

    private static string Nested(string sourceTypeName) => sourceTypeName["FluentIcons".Length..];

    /// <summary>Glyph values are private-use code points; escape them so failures are readable.</summary>
    private static string Describe(string value)
        => "\"" + string.Concat(value.Select(ch =>
            ch is >= ' ' and <= '~' ? ch.ToString() : $"\\u{(int)ch:x4}")) + "\"";

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
