using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AniSprinkles.UITests;

/// <summary>
/// The emulator the app runs on, reached through <c>adb</c>, for the one thing DevFlow's agent cannot
/// do here: a real touch. The agent's taps invoke an element's action directly, and switching a Shell
/// tab that way skips the pushed page's <c>OnNavigatedTo</c> (see <see cref="App.GoToTabAsync"/>).
/// Temporary: DevFlow's pointer sessions do real touches, and once a release ships them this class
/// can go (#228).
/// </summary>
internal sealed partial class AndroidDevice(string adbPath, string? serial)
{
    private double? _density;

    /// <summary>
    /// <c>--test-parameter Device=&lt;serial&gt;</c> picks the emulator (CI has one, so it may be
    /// omitted there); adb is found through <c>ANDROID_HOME</c>, <c>ANDROID_SDK_ROOT</c> or the PATH.
    /// </summary>
    public static AndroidDevice FromParameters()
    {
        var serial = TestContext.Parameters.Get("Device", (string?)null);
        return new AndroidDevice(FindAdb(), string.IsNullOrWhiteSpace(serial) ? null : serial);
    }

    /// <summary>
    /// Pixels per device-independent unit, from <c>wm density</c> (dpi / 160). DevFlow reports
    /// bounds in device-independent units; <c>input tap</c> takes pixels.
    /// </summary>
    public async Task<double> GetDensityAsync()
    {
        if (_density is { } known)
        {
            return known;
        }

        var output = await RunAsync("shell wm density");
        // "Physical density: 480", plus "Override density: 420" when one is set, which wins.
        var matches = DensityPattern().Matches(output);
        Assert.That(matches, Is.Not.Empty, $"Could not read the screen density from: {output}");
        var dpi = int.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture);
        _density = dpi / 160.0;
        return _density.Value;
    }

    /// <summary>Runs one adb command against this device and returns its output; fails the test on a non-zero exit.</summary>
    public async Task<string> RunAsync(string arguments)
    {
        var target = serial is null ? string.Empty : $"-s {serial} ";
        var info = new ProcessStartInfo(adbPath, target + arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {adbPath}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        Assert.That(process.ExitCode, Is.Zero, $"adb {target}{arguments} failed: {await stderr}{output}");
        return output;
    }

    private static string FindAdb()
    {
        var exe = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        foreach (var root in new[] { Environment.GetEnvironmentVariable("ANDROID_HOME"), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") })
        {
            if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, "platform-tools", exe)))
            {
                return Path.Combine(root, "platform-tools", exe);
            }
        }

        // On the PATH, as it is on a GitHub runner and wherever platform-tools was installed normally.
        return exe;
    }

    [GeneratedRegex(@"density:\s*(\d+)")]
    private static partial Regex DensityPattern();
}
