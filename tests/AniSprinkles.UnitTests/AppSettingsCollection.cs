namespace AniSprinkles.UnitTests;

/// <summary>
/// <see cref="AniSprinkles.Utilities.AppSettings"/> is process-wide static state, so test classes
/// that write to it must not run concurrently with each other. xUnit runs classes in parallel by
/// default; sharing this collection serialises them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppSettingsCollection
{
    public const string Name = "AppSettings";
}
