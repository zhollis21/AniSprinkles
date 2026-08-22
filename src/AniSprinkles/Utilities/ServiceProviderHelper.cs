namespace AniSprinkles.Utilities;

/// <summary>
/// Shared utility for dependency injection service provider resolution.
/// Handles the defensive fallback pattern needed for MAUI apps, especially on Android
/// where Application.Handler may not be fully wired during early page creation.
/// </summary>
public static class ServiceProviderHelper
{
    private static IServiceProvider? _cached;

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// Resolves from multiple sources to handle Android activity restart scenarios,
    /// and caches the first non-null result so subsequent calls are O(1).
    /// </summary>
    /// <returns>The service provider</returns>
    /// <exception cref="InvalidOperationException">Thrown if service provider is not available</exception>
    /// <remarks>
    /// The resolution order is:
    /// 1. Application.Current?.Handler?.MauiContext?.Services (primary, available after handler wiring)
    /// 2. IPlatformApplication.Current?.Services (fallback, available early in app startup)
    ///
    /// A null resolution does <b>not</b> poison the cache — the next call will re-resolve.
    /// This matters for any call path that reaches the helper before DI is wired
    /// (static initializers, early constructors); they throw once and can succeed later.
    /// </remarks>
    public static IServiceProvider GetServiceProvider()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var provider = Application.Current?.Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services;

        if (provider is null)
        {
            throw new InvalidOperationException(
                "Service provider not available. Ensure your MauiApp is properly configured with dependency injection services.");
        }

        _cached = provider;
        return provider;
    }
}
