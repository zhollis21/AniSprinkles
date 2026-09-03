using System.Net;

namespace AniSprinkles.Services;

/// <summary>
/// Pure mapping from an AniList HTTP/GraphQL failure to a classified <see cref="ApiErrorKind"/>.
/// Extracted from <see cref="AniListClient"/> so the (MAUI-free) classification logic can be
/// unit-tested directly — including the 429 → <see cref="ApiErrorKind.RateLimited"/> mapping.
/// </summary>
public static class AniListErrorClassifier
{
    public static ApiErrorKind ClassifyHttpError(HttpStatusCode statusCode, string? apiMessage)
    {
        // Known AniList outage pattern: 403 with a human-readable "disabled" message.
        if (apiMessage is not null && ContainsOutageMarker(apiMessage))
        {
            return ApiErrorKind.ServiceOutage;
        }

        // AniList returns HTTP 400 (not 401) with an "Invalid token" body for a rejected OAuth token,
        // so classify on the message, not just the status code. Mirrors ClassifyGraphQlError below.
        if (apiMessage is not null &&
            (apiMessage.Contains("Invalid token", StringComparison.OrdinalIgnoreCase) ||
             apiMessage.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)))
        {
            return ApiErrorKind.Authentication;
        }

        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => ApiErrorKind.RateLimited,
            HttpStatusCode.Unauthorized => ApiErrorKind.Authentication,
            HttpStatusCode.Forbidden when apiMessage is null => ApiErrorKind.Authentication,
            HttpStatusCode.NotFound => ApiErrorKind.NotFound,
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.BadGateway or
            HttpStatusCode.GatewayTimeout => ApiErrorKind.ServiceOutage,
            _ => ApiErrorKind.Unknown,
        };
    }

    public static ApiErrorKind ClassifyGraphQlError(string message)
    {
        if (ContainsOutageMarker(message))
        {
            return ApiErrorKind.ServiceOutage;
        }

        if (message.Contains("Invalid token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return ApiErrorKind.Authentication;
        }

        // AniList also surfaces missing ids as a GraphQL-level "Not Found." error in some responses,
        // where those words are the entire message.
        //
        // Matched exactly rather than as a substring (#158). NotFound is the most consequential
        // classification we make — it is the only kind that is non-retryable AND suppressed from
        // Sentry — so a substring hit anywhere in server-supplied text turned any transient failure
        // that happened to contain the words into a permanent, silent dead end. Unknown is the
        // honest answer for text we don't recognise, and it stays retryable and reported.
        if (IsNotFoundMessage(message))
        {
            return ApiErrorKind.NotFound;
        }

        return ApiErrorKind.Unknown;
    }

    /// <summary>
    /// Whether the whole message is AniList's not-found response, allowing for the trailing period
    /// it usually carries and whatever whitespace survived transport.
    /// </summary>
    private static bool IsNotFoundMessage(string message)
    {
        var trimmed = message.Trim().TrimEnd('.').Trim();
        return trimmed.Equals("Not Found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOutageMarker(string message) =>
        message.Contains("temporarily disabled", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("stability issues", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("under maintenance", StringComparison.OrdinalIgnoreCase);
}
