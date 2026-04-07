namespace AniSprinkles.Utilities;

/// <summary>
/// Shared utility for dependency injection service provider resolution.
/// Handles the defensive fallback pattern needed for MAUI apps, especially on Android
/// where Application.Handler may not be fully wired during early page creation.
/// </summary>
public static class ServiceProviderHelper
{
    /// <summary>
    /// Cached service provider that's computed once and reused.
    /// Uses Lazy&lt;T&gt; for thread-safe initialization.
    /// </summary>
    private static readonly Lazy<IServiceProvider?> CachedServiceProvider = new(
        () => Application.Current?.Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services
    );

    /// <summary>
    /// Gets the cached service provider for dependency injection.
    /// Resolves from multiple sources to handle Android activity restart scenarios.
    /// </summary>
    /// <returns>The service provider</returns>
    /// <exception cref="InvalidOperationException">Thrown if service provider is not available</exception>
    /// <remarks>
    /// The resolution order is:
    /// 1. Application.Current?.Handler?.MauiContext?.Services (primary, available after handler wiring)
    /// 2. IPlatformApplication.Current?.Services (fallback, available early in app startup)
    /// 
    /// This pattern is recommended by Microsoft for MAUI apps and handles edge cases
    /// where Shell pages are built before the Application handler is fully initialized.
    /// </remarks>
    public static IServiceProvider GetServiceProvider()
    {
        var provider = CachedServiceProvider.Value;
        if (provider is null)
        {
            throw new InvalidOperationException(
                "Service provider not available. Ensure your MauiApp is properly configured with dependency injection services.");
        }

        return provider;
    }
}
