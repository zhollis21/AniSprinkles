using System.Reflection;
using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.Services.Maui;

/// <summary>
/// <see cref="IAppEnvironment"/> over MAUI Essentials and the app assembly's build stamps, for the
/// header of a diagnostic report (#112) and the Settings version label (#207).
/// <para>
/// Lives on this side of the seam because <c>AppInfo</c> and <c>DeviceInfo</c> throw rather than
/// degrade on the plain <c>net11.0</c> test TFM — reading them from Core would make the whole report
/// flow untestable. Each property is guarded anyway: a report is worth sending with an unknown
/// device model, and is worth nothing if collecting the model threw.
/// </para>
/// </summary>
public sealed class MauiAppEnvironment : IAppEnvironment
{
    private const string Unknown = "unknown";

    // Lazy because it reflects over the assembly and nothing about it changes while the process runs.
    private readonly Lazy<BuildIdentity> _build = new(ReadBuild);

    public string AppVersion => Safe(() => $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})");

    /// <inheritdoc/>
    /// <remarks>
    /// Release trims the app, so these attributes have to survive ILLink; they do because the
    /// assembly's own custom attributes are kept with it, and <see cref="AssemblyMetadataAttribute"/>
    /// is read through the trim-safe generic overload.
    /// </remarks>
    public BuildIdentity Build => _build.Value;

    public string BuildConfiguration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    public string Device => Safe(() => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}");

    public string OsVersion => Safe(() => $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}");

    /// <summary>
    /// The version from <c>AppInfo</c>, and the source stamps <c>AniSprinkles.csproj</c>'s
    /// <c>_StampBuildSource</c> target writes as <see cref="AssemblyMetadataAttribute"/>s on this
    /// assembly. A stamp that is missing reads as empty, which the label treats as unknown.
    /// </summary>
    private static BuildIdentity ReadBuild()
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            foreach (var attribute in typeof(MauiAppEnvironment).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                metadata[attribute.Key] = attribute.Value;
            }
        }
        catch
        {
            // Same reasoning as Safe below: a version label is not worth a crash.
        }

        string? Stamp(string key) => metadata.GetValueOrDefault(key);

        return new BuildIdentity(
            Safe(() => AppInfo.Current.VersionString),
            Safe(() => AppInfo.Current.BuildString),
            Stamp("SourceBranch"),
            Stamp("SourceCommit"),
            Stamp("SourceDescribe"),
            IsDirty: Stamp("SourceDirty") == "true",
            IsStoreBuild: Stamp("StoreBuild") == "true");
    }

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
