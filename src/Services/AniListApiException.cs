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
    RateLimited,
}

/// <summary>
/// A typed exception thrown by <see cref="AniListClient"/> that carries a
/// classified <see cref="Kind"/> so page models can display user-friendly
/// error messages without string-matching.
/// </summary>
public partial class AniListApiException : Exception
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
        ApiErrorKind.ServiceOutage => "AniList is Down",
        ApiErrorKind.Network => "No Internet Connection",
        ApiErrorKind.Authentication => "Session Expired",
        ApiErrorKind.RateLimited => "Slow Down a Sec",
        _ => "Something Went Wrong",
    };

    /// <summary>
    /// Returns a longer subtitle with guidance for the user.
    /// </summary>
    public string UserSubtitle => Kind switch
    {
        ApiErrorKind.ServiceOutage => "AniList's servers are having trouble. This is on their end, not yours — we'll retry automatically once they're back. Feel free to check anilist.co or @AniList on social for updates.",
        ApiErrorKind.Network => "Check your connection and try again.",
        ApiErrorKind.Authentication => "Please sign in again to continue.",
        ApiErrorKind.RateLimited => "AniList is asking us to slow down. Give it a moment, then try again.",
        _ => "An unexpected error occurred. Try again or check back later.",
    };
}
