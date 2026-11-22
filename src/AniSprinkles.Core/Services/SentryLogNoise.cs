using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// Error logs that should not become Sentry events, shaped for Sentry's <c>AddLogEntryFilter</c>, which
/// runs before the event is built. <c>BeforeSend</c> would be too late: it runs after the event
/// processors have already done their work.
/// </summary>
public static class SentryLogNoise
{
    private const string PlatformViewNull = "PlatformView cannot be null here";

    /// <summary>Returns <c>true</c> to drop the log entry from Sentry. It still reaches every other logger.</summary>
    public static bool Filter(string categoryName, LogLevel logLevel, EventId eventId, Exception? exception) =>
        IsImageSourceLoadAfterDisconnect(categoryName, exception?.GetType(), exception?.Message, exception?.StackTrace);

    /// <summary>
    /// MAUI 11.0.10 loads a button's <c>ImageSource</c> fire-and-forget. When the button is torn down
    /// first (the rating prompt closed by a quick Skip, #230), the loader finds no platform view and
    /// logs the <see cref="InvalidOperationException"/> at Error. Nothing is wrong: the icon belongs to
    /// a view that no longer exists. Matched by its loader frame as well as its message, because the
    /// same message comes from any handler used after disconnect, and those can be real bugs.
    /// Remove once MAUI stops logging it; the blocked follow-up to #230 tracks that.
    /// </summary>
    public static bool IsImageSourceLoadAfterDisconnect(
        string categoryName, Type? exceptionType, string? message, string? stackTrace) =>
        categoryName.StartsWith("Microsoft.Maui", StringComparison.Ordinal)
        && exceptionType == typeof(InvalidOperationException)
        && message == PlatformViewNull
        && stackTrace?.Contains("ImageSourcePartLoader", StringComparison.Ordinal) == true;
}
