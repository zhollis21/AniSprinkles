using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// Fixed values for the report header and the Settings version label. The real implementation reads MAUI Essentials, which throws
/// on the plain <c>net11.0</c> test TFM — which is the whole reason <see cref="IAppEnvironment"/>
/// exists.
/// </summary>
public sealed class StubAppEnvironment : IAppEnvironment
{
    public string AppVersion { get; set; } = "1.2.3 (45)";

    public BuildIdentity Build { get; set; } =
        new("1.2.3", "45", "main", "6f69547a1b2c3d4e5f60718293a4b5c6d7e8f901", "v1.2.3-0-g6f69547", IsDirty: false, IsStoreBuild: false);

    public string BuildConfiguration { get; set; } = "Release";

    public string Device { get; set; } = "Google Pixel 7";

    public string OsVersion { get; set; } = "Android 15";
}
