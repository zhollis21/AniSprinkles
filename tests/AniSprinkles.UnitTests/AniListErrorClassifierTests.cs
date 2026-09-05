using System.Net;

namespace AniSprinkles.UnitTests;

public class AniListErrorClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, ApiErrorKind.Authentication)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ApiErrorKind.ServiceOutage)]
    [InlineData(HttpStatusCode.BadGateway, ApiErrorKind.ServiceOutage)]
    [InlineData(HttpStatusCode.GatewayTimeout, ApiErrorKind.ServiceOutage)]
    [InlineData(HttpStatusCode.NotFound, ApiErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorKind.Unknown)]
    public void ClassifyHttpError_ByStatusCode_MapsToExpectedKind(HttpStatusCode status, ApiErrorKind expected)
    {
        Assert.Equal(expected, AniListErrorClassifier.ClassifyHttpError(status, apiMessage: null));
    }

    [Fact]
    public void ClassifyHttpError_ForbiddenWithoutMessage_IsAuthentication()
    {
        Assert.Equal(ApiErrorKind.Authentication, AniListErrorClassifier.ClassifyHttpError(HttpStatusCode.Forbidden, apiMessage: null));
    }

    [Fact]
    public void ClassifyHttpError_ForbiddenWithMessage_IsNotTreatedAsAuthentication()
    {
        Assert.Equal(ApiErrorKind.Unknown, AniListErrorClassifier.ClassifyHttpError(HttpStatusCode.Forbidden, "some other error"));
    }

    [Theory]
    [InlineData("AniList is temporarily disabled for stability")]
    [InlineData("The API is under maintenance")]
    [InlineData("We're having stability issues")]
    public void ClassifyHttpError_OutageMessage_OverridesStatusCode(string message)
    {
        // Even on a 403, an outage marker in the body wins.
        Assert.Equal(ApiErrorKind.ServiceOutage, AniListErrorClassifier.ClassifyHttpError(HttpStatusCode.Forbidden, message));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Invalid token")]
    [InlineData(HttpStatusCode.BadRequest, "invalid token.")]
    [InlineData(HttpStatusCode.OK, "Unauthorized")]
    public void ClassifyHttpError_InvalidTokenMessage_IsAuthentication(HttpStatusCode status, string message)
    {
        // AniList returns 400 (not 401) with an "Invalid token" body, so the message must win over the
        // status code — otherwise this falls through to Unknown and never gets the silent auth retry.
        Assert.Equal(ApiErrorKind.Authentication, AniListErrorClassifier.ClassifyHttpError(status, message));
    }

    [Theory]
    [InlineData("Invalid token provided", ApiErrorKind.Authentication)]
    [InlineData("Unauthorized", ApiErrorKind.Authentication)]
    [InlineData("AniList is under maintenance", ApiErrorKind.ServiceOutage)]
    [InlineData("Not Found.", ApiErrorKind.NotFound)]
    [InlineData("Validation error: bad field", ApiErrorKind.Unknown)]
    public void ClassifyGraphQlError_ByMessage_MapsToExpectedKind(string message, ApiErrorKind expected)
    {
        Assert.Equal(expected, AniListErrorClassifier.ClassifyGraphQlError(message));
    }

    // ── The NotFound match is exact, not a substring (#158) ──────────

    [Theory]
    [InlineData("Not Found.")]
    [InlineData("Not Found")]
    [InlineData("not found.")]
    [InlineData("NOT FOUND")]
    [InlineData("  Not Found.  ")]
    public void ClassifyGraphQlError_AniListsOwnNotFoundMessage_IsNotFound(string message)
    {
        // These are the shapes AniList actually sends for a missing id — the whole message is the
        // words, optionally with the trailing period and whatever whitespace survived transport.
        Assert.Equal(ApiErrorKind.NotFound, AniListErrorClassifier.ClassifyGraphQlError(message));
    }

    [Theory]
    [InlineData("Internal server error: upstream Not Found while resolving media")]
    [InlineData("Not Found in cache, falling back")]
    [InlineData("Studio Not Found for this relation, skipping")]
    [InlineData("Timed out: Not Found after 3 attempts")]
    public void ClassifyGraphQlError_NotFoundBuriedInALongerMessage_IsNotTreatedAsNotFound(string message)
    {
        // #158. A substring match anywhere in server-supplied text used to be enough, which turned a
        // transient failure into a permanent, non-retryable, unreportable dead end. Unknown is the
        // honest answer for text we don't recognise — it stays retryable and it reaches Sentry.
        Assert.Equal(ApiErrorKind.Unknown, AniListErrorClassifier.ClassifyGraphQlError(message));
    }

    [Fact]
    public void ClassifyGraphQlError_AnOutageMessageMentioningNotFound_IsStillAnOutage()
    {
        // Ordering guard: the outage check runs first and must keep winning, or a maintenance
        // notice that happens to contain the words would be misfiled.
        Assert.Equal(
            ApiErrorKind.ServiceOutage,
            AniListErrorClassifier.ClassifyGraphQlError("AniList is under maintenance; Not Found errors are expected"));
    }

    [Theory]
    [InlineData(ApiErrorKind.ServiceOutage, "AniList is Down")]
    [InlineData(ApiErrorKind.Network, "No Internet Connection")]
    [InlineData(ApiErrorKind.Authentication, "Session Expired")]
    [InlineData(ApiErrorKind.RateLimited, "Slow Down a Sec")]
    [InlineData(ApiErrorKind.NotFound, "Entry Unavailable")]
    [InlineData(ApiErrorKind.Unknown, "Something Went Wrong")]
    public void AniListApiException_EveryKind_HasFriendlyTitleAndNonEmptySubtitle(ApiErrorKind kind, string expectedTitle)
    {
        var ex = new AniListApiException(kind, "raw message");

        // Every kind must map to a curated title (not leak the raw message) and offer guidance.
        Assert.Equal(expectedTitle, ex.UserTitle);
        Assert.False(string.IsNullOrWhiteSpace(ex.UserSubtitle));
    }
}
