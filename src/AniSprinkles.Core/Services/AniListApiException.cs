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

    /// <summary>
    /// AniList returned 404, or a GraphQL error whose entire message is "Not Found", for an id we
    /// requested — the id did not resolve. This can be an AniList-side dangling reference (an id from
    /// a relation, recommendation, or list entry that no longer exists) or a type-constrained lookup
    /// (e.g. a non-anime id passed to <c>Media(id:, type: ANIME)</c>).
    /// <para>
    /// Usually the result won't change on a retry — but it is still offered, and still reported
    /// (#158). We could not previously tell a genuine 404 from a transient failure misfiled as one,
    /// because this was the single kind that produced neither a Sentry event nor a shareable report,
    /// so there was nothing to tell them apart with. See <see cref="AniListErrorClassifier"/> for the
    /// matching rule that stopped arbitrary server text landing here.
    /// </para>
    /// </summary>
    NotFound,
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
        ApiErrorKind.ServiceOutage => "AniList is Down",
        ApiErrorKind.Network => "No Internet Connection",
        ApiErrorKind.Authentication => "Session Expired",
        ApiErrorKind.RateLimited => "Slow Down a Sec",
        ApiErrorKind.NotFound => "Entry Unavailable",
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
        ApiErrorKind.NotFound => "We couldn't find this on AniList — it may have been removed or merged into another entry.",
        _ => "An unexpected error occurred. Try again or check back later.",
    };

    /// <summary>
    /// Returns the Fluent icon glyph appropriate for this error kind.
    /// </summary>
    public string IconGlyph => Kind switch
    {
        ApiErrorKind.ServiceOutage => Glyphs.Regular.CloudDismiss24,
        ApiErrorKind.Network => Glyphs.Regular.WifiOff24,
        ApiErrorKind.Authentication => Glyphs.Regular.LockClosed24,
        ApiErrorKind.RateLimited => Glyphs.Regular.Clock24,
        ApiErrorKind.NotFound => Glyphs.Regular.DismissCircle24,
        _ => Glyphs.Regular.ErrorCircle24,
    };
}
