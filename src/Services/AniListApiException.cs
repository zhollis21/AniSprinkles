namespace AniSprinkles.Services;

/// <summary>
/// Classifies the kind of AniList API failure so the UI can show
/// context-appropriate error states.
/// </summary>
public enum ApiErrorKind
{
    Unknown,
    ServiceOutage,
    Network,
    Authentication,
}

/// <summary>
/// A typed exception thrown by <see cref="AniListClient"/> that carries a
/// classified <see cref="Kind"/> so page models can display user-friendly
/// error messages without string-matching.
/// </summary>
public class AniListApiException : Exception
{
    public ApiErrorKind Kind { get; }

    public AniListApiException(ApiErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    /// <summary>
    /// Returns a short, user-friendly title for the error.
    /// </summary>
    public string UserTitle => Kind switch
    {
        ApiErrorKind.ServiceOutage => "AniList is Temporarily Down",
        ApiErrorKind.Network => "No Internet Connection",
        ApiErrorKind.Authentication => "Session Expired",
        _ => "Something Went Wrong",
    };

    /// <summary>
    /// Returns a longer subtitle with guidance for the user.
    /// </summary>
    public string UserSubtitle => Kind switch
    {
        ApiErrorKind.ServiceOutage => "The AniList servers are having trouble right now. This isn't your fault — try again in a few minutes.",
        ApiErrorKind.Network => "Check your connection and try again.",
        ApiErrorKind.Authentication => "Please sign in again to continue.",
        _ => "An unexpected error occurred. Try again or check back later.",
    };

    /// <summary>
    /// Returns the Fluent icon glyph appropriate for this error kind.
    /// </summary>
    public string IconGlyph => Kind switch
    {
        ApiErrorKind.ServiceOutage => IconFont.Maui.FluentIcons.FluentIconsRegular.CloudDismiss24,
        ApiErrorKind.Network => IconFont.Maui.FluentIcons.FluentIconsRegular.WifiOff24,
        ApiErrorKind.Authentication => IconFont.Maui.FluentIcons.FluentIconsRegular.LockClosed24,
        _ => IconFont.Maui.FluentIcons.FluentIconsRegular.ErrorCircle24,
    };
}
