namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// The handful of environment facts a diagnostic report needs in its header (#112).
/// <para>
/// A seam because every one of these comes from MAUI Essentials — <c>AppInfo</c>, <c>DeviceInfo</c> —
/// which throw rather than degrade on the plain <c>net10.0</c> test TFM. Reading them straight from a
/// page model would make the whole report flow untestable.
/// </para>
/// </summary>
public interface IAppEnvironment
{
    /// <summary>Version and build, e.g. <c>0.13.0 (142)</c>.</summary>
    string AppVersion { get; }

    /// <summary><c>Debug</c> or <c>Release</c>. Load on this repo's behaviour genuinely diverges
    /// between the two — log levels, CI stubs, fault injection — so a report that does not say which
    /// one it came from is missing a fact the reader would otherwise have to guess.</summary>
    string BuildConfiguration { get; }

    /// <summary>Manufacturer and model, e.g. <c>Google Pixel 7</c>.</summary>
    string Device { get; }

    /// <summary>Platform and version, e.g. <c>Android 15</c>.</summary>
    string OsVersion { get; }
}
