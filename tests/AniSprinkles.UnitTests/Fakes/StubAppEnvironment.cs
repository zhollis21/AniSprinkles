using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// Fixed values for the report header. The real implementation reads MAUI Essentials, which throws
/// on the plain <c>net10.0</c> test TFM — which is the whole reason <see cref="IAppEnvironment"/>
/// exists.
/// </summary>
public sealed class StubAppEnvironment : IAppEnvironment
{
    public string AppVersion { get; set; } = "1.2.3 (45)";

    public string BuildConfiguration { get; set; } = "Release";

    public string Device { get; set; } = "Google Pixel 7";

    public string OsVersion { get; set; } = "Android 15";
}
