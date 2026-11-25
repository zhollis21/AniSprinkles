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
    private static readonly Regex DeviceDisplayReference = new(@"\bI?DeviceDisplay\b", RegexOptions.Compiled);

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

    [Test]
    public void SourceFiles_IncludeCore()
    {
        // Core ships inside the app and references MAUI Essentials, so a read there races just the same.
        Assert.That(AppSourceFiles(), Does.Contain("src/AniSprinkles.Core/Services/SentryLogNoise.cs"));
    }

    [TestCase("var d = DeviceDisplay.MainDisplayInfo;", 1, TestName = "Flags a static read")]
    [TestCase("var d = DeviceDisplay.Current.MainDisplayInfo;", 1, TestName = "Flags a Current read")]
    [TestCase("using static Microsoft.Maui.Devices.DeviceDisplay;", 1, TestName = "Flags a using static")]
    [TestCase("public Popup(IDeviceDisplay display) { }", 1, TestName = "Flags the injected interface")]
    [TestCase("var u = \"https://anilist.co/\"; var d = DeviceDisplay.MainDisplayInfo;", 1, TestName = "Flags a read after a URL")]
    [TestCase("var s = $\"{DeviceDisplay.MainDisplayInfo.Width}\";", 1, TestName = "Flags a read inside an interpolated string")]
    [TestCase("// DeviceDisplay is not safe here", 1, TestName = "Flags a comment too, so nothing can hide behind one")]
    [TestCase("var MyDeviceDisplayHelper = 1;", 0, TestName = "Ignores a longer identifier")]
    [TestCase("var w = ScreenMetrics.Read().WidthDip; // MAUI's display API", 0, TestName = "Ignores a comment that avoids the name")]
    public void FindReferences_DetectsEachShape(string line, int expected)
    {
        Assert.That(FindReferences([line], "fragment.cs"), Has.Count.EqualTo(expected));
    }

    // Every line counts, comments included. Telling code from comments needs a C# lexer (a "//" can sit
    // in a string, a read can sit in an interpolation), and a guard that guesses wrong lets a read
    // through. Comments in src/ call it "MAUI's display API" instead.
    internal static List<string> FindReferences(IReadOnlyList<string> lines, string relativePath)
    {
        var offenders = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (DeviceDisplayReference.IsMatch(lines[i]))
            {
                offenders.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");
            }
        }

        return offenders;
    }

    // Both projects that ship in the app: Core references MAUI Essentials too.
    private static List<string> AppSourceFiles()
    {
        var root = XamlFileValidationTests.RepoRoot.Value;
        return new[] { "AniSprinkles", "AniSprinkles.Core" }
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(root, "src", project), "*.*", SearchOption.AllDirectories))
            .Where(f => Path.GetExtension(f) is ".cs" or ".xaml")
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel => !rel.Contains("/bin/") && !rel.Contains("/obj/"))
            .ToList();
    }
}
