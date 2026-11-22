using System.Text.RegularExpressions;

namespace AniSprinkles.UnitTests;

// Keeps the app from reading MAUI's DeviceDisplay (#230). On Android, MAUI 11.0.10's
// DeviceDisplayImplementation.GetDefaultDisplay() wraps the window-manager service in `using`, which
// disposes a managed peer that Java.Interop shares between every caller. Two threads inside it at once
// can leave one holding a deleted JNI global reference, and the runtime aborts the process.
//
// Sentry.Maui reads DeviceDisplay.MainDisplayInfo while building every event, on whatever thread
// captured it, and serializes only its own reads with a private lock. So any read of ours, on the UI
// thread, can race a background Error log and take the app down. The Up Next sheet's constructor did
// exactly that right after the rating prompt's Skip. Screen size comes from ScreenMetrics instead.
//
// Remove this guard once MAUI no longer disposes the shared peer (dotnet/maui#40510, tracked in
// #234).
public class DeviceDisplayGuardTests
{
    private static readonly Regex DeviceDisplayReference = new(@"\bDeviceDisplay\b", RegexOptions.Compiled);

    [Test]
    public void AppSourceFiles_AreFound()
    {
        // Without this, the guard below could pass vacuously after a move.
        Assert.That(AppSourceFiles(), Has.Count.GreaterThan(50));
    }

    [Test]
    public void AppSource_NeverReadsDeviceDisplay()
    {
        var root = XamlFileValidationTests.RepoRoot.Value;
        var offenders = new List<string>();
        foreach (var relativePath in AppSourceFiles())
        {
            offenders.AddRange(FindReferences(File.ReadAllLines(Path.Combine(root, relativePath)), relativePath));
        }

        Assert.That(offenders, Is.Empty,
            "Don't read DeviceDisplay in the app: on Android it races Sentry's event processing and can abort "
            + "the process (#230). Use ScreenMetrics instead." + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [TestCase("var d = DeviceDisplay.MainDisplayInfo;", 1, TestName = "Flags a static read")]
    [TestCase("var d = DeviceDisplay.Current.MainDisplayInfo;", 1, TestName = "Flags a Current read")]
    [TestCase("using static Microsoft.Maui.Devices.DeviceDisplay;", 1, TestName = "Flags a using static")]
    [TestCase("// DeviceDisplay is not safe here", 0, TestName = "Ignores a line comment")]
    [TestCase("var w = ScreenMetrics.Read().WidthDip; // not DeviceDisplay", 0, TestName = "Ignores a trailing comment")]
    [TestCase("var MyDeviceDisplayHelper = 1;", 0, TestName = "Ignores a longer identifier")]
    public void FindReferences_DetectsEachShape(string line, int expected)
    {
        Assert.That(FindReferences([line], "fragment.cs"), Has.Count.EqualTo(expected));
    }

    internal static List<string> FindReferences(IReadOnlyList<string> lines, string relativePath)
    {
        var offenders = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            // Comments may explain the rule; only code counts. Naive about "//" inside strings, which is
            // fine for this name.
            var line = lines[i];
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            var code = comment >= 0 ? line[..comment] : line;
            if (DeviceDisplayReference.IsMatch(code))
            {
                offenders.Add($"{relativePath}:{i + 1}: {line.Trim()}");
            }
        }

        return offenders;
    }

    private static List<string> AppSourceFiles()
    {
        var root = XamlFileValidationTests.RepoRoot.Value;
        var appDir = Path.Combine(root, "src", "AniSprinkles");
        return Directory.EnumerateFiles(appDir, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".cs" or ".xaml")
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel => !rel.Contains("/bin/") && !rel.Contains("/obj/"))
            .ToList();
    }
}
