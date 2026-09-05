using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.Services.Maui;

/// <summary>
/// <see cref="IAppEnvironment"/> over MAUI Essentials, for the header of a diagnostic report (#112).
/// <para>
/// Lives on this side of the seam because <c>AppInfo</c> and <c>DeviceInfo</c> throw rather than
/// degrade on the plain <c>net10.0</c> test TFM — reading them from Core would make the whole report
/// flow untestable. Each property is guarded anyway: a report is worth sending with an unknown
/// device model, and is worth nothing if collecting the model threw.
/// </para>
/// </summary>
public sealed class MauiAppEnvironment : IAppEnvironment
{
    private const string Unknown = "unknown";

    public string AppVersion => Safe(() => $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})");

    public string BuildConfiguration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    public string Device => Safe(() => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}");

    public string OsVersion => Safe(() => $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}");

    /// <summary>
    /// Deliberately broad, and deliberately silent. These are header decorations on a report the user
    /// is sending because something else already went wrong; one of them throwing must not be the
    /// reason the report never arrives, and logging it here would only pad the very log being sent.
    /// </summary>
    private static string Safe(Func<string> read)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? Unknown : value;
        }
        catch
        {
            return Unknown;
        }
    }
}
