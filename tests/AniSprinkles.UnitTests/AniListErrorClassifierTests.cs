using System.Net;

namespace AniSprinkles.UnitTests;

public class AniListErrorClassifierTests
{
    [TestCase(HttpStatusCode.TooManyRequests, ApiErrorKind.RateLimited)]
    [TestCase(HttpStatusCode.Unauthorized, ApiErrorKind.Authentication)]
    [TestCase(HttpStatusCode.ServiceUnavailable, ApiErrorKind.ServiceOutage)]
    [TestCase(HttpStatusCode.BadGateway, ApiErrorKind.ServiceOutage)]
    [TestCase(HttpStatusCode.GatewayTimeout, ApiErrorKind.ServiceOutage)]
    [TestCase(HttpStatusCode.NotFound, ApiErrorKind.NotFound)]
    [TestCase(HttpStatusCode.InternalServerError, ApiErrorKind.Unknown)]
    public void ClassifyHttpError_ByStatusCode_MapsToExpectedKind(HttpStatusCode status, ApiErrorKind expected)
    {
        Assert.That(AniListErrorClassifier.ClassifyHttpError(status, apiMessage: null), Is.EqualTo(expected));
    }

    [Test]
    public void ClassifyHttpError_ForbiddenWithoutMessage_IsAuthentication()
    {
        Assert.That(AniListErrorClassifier.ClassifyHttpError(HttpStatusCode.Forbidden, apiMessage: null), Is.EqualTo(ApiErrorKind.Authentication));
    }

    [Test]
    public void ClassifyHttpError_ForbiddenWithMessage_IsNotTreatedAsAuthentication()
    {
        Assert.That(AniListErrorClassifier.ClassifyHttpError(HttpStatusCode.Forbidden, "some other error"), Is.EqualTo(ApiErrorKind.Unknown));
    }

    [TestCase("AniList is temporarily disabled for stability")]
    [TestCase("The API is under maintenance")]
    [TestCase("We're having stability issues")]
    public void ClassifyHttpError_OutageMessage_OverridesStatusCode(string message)
    {
        // Even on a 403, an outage marker in the body wins.
        Assert.That(AniListErrorClassifier.ClassifyHttpError(HttpStatusCode.Forbidden, message), Is.EqualTo(ApiErrorKind.ServiceOutage));
    }

    [TestCase(HttpStatusCode.BadRequest, "Invalid token")]
    [TestCase(HttpStatusCode.BadRequest, "invalid token.")]
    [TestCase(HttpStatusCode.OK, "Unauthorized")]
    public void ClassifyHttpError_InvalidTokenMessage_IsAuthentication(HttpStatusCode status, string message)
    {
        // AniList returns 400 (not 401) with an "Invalid token" body, so the message must win over the
        // status code — otherwise this falls through to Unknown and never gets the silent auth retry.
        Assert.That(AniListErrorClassifier.ClassifyHttpError(status, message), Is.EqualTo(ApiErrorKind.Authentication));
    }

    [TestCase("Invalid token provided", ApiErrorKind.Authentication)]
    [TestCase("Unauthorized", ApiErrorKind.Authentication)]
    [TestCase("AniList is under maintenance", ApiErrorKind.ServiceOutage)]
    [TestCase("Not Found.", ApiErrorKind.NotFound)]
    [TestCase("Validation error: bad field", ApiErrorKind.Unknown)]
    public void ClassifyGraphQlError_ByMessage_MapsToExpectedKind(string message, ApiErrorKind expected)
    {
        Assert.That(AniListErrorClassifier.ClassifyGraphQlError(message), Is.EqualTo(expected));
    }

    // ── The NotFound match is exact, not a substring (#158) ──────────

    [TestCase("Not Found.")]
    [TestCase("Not Found")]
    [TestCase("not found.")]
    [TestCase("NOT FOUND")]
    [TestCase("  Not Found.  ")]
    public void ClassifyGraphQlError_AniListsOwnNotFoundMessage_IsNotFound(string message)
    {
        // These are the shapes AniList actually sends for a missing id — the whole message is the
        // words, optionally with the trailing period and whatever whitespace survived transport.
        Assert.That(AniListErrorClassifier.ClassifyGraphQlError(message), Is.EqualTo(ApiErrorKind.NotFound));
    }

    [TestCase("Internal server error: upstream Not Found while resolving media")]
    [TestCase("Not Found in cache, falling back")]
    [TestCase("Studio Not Found for this relation, skipping")]
    [TestCase("Timed out: Not Found after 3 attempts")]
    public void ClassifyGraphQlError_NotFoundBuriedInALongerMessage_IsNotTreatedAsNotFound(string message)
    {
        // #158. A substring match anywhere in server-supplied text used to be enough, which turned a
        // transient failure into a permanent, non-retryable, unreportable dead end. Unknown is the
        // honest answer for text we don't recognise — it stays retryable and it reaches Sentry.
        Assert.That(AniListErrorClassifier.ClassifyGraphQlError(message), Is.EqualTo(ApiErrorKind.Unknown));
    }

    [Test]
    public void ClassifyGraphQlError_AnOutageMessageMentioningNotFound_IsStillAnOutage()
    {
        // Ordering guard: the outage check runs first and must keep winning, or a maintenance
        // notice that happens to contain the words would be misfiled.
        Assert.That(
            AniListErrorClassifier.ClassifyGraphQlError("AniList is under maintenance; Not Found errors are expected"),
            Is.EqualTo(ApiErrorKind.ServiceOutage));
    }

    [TestCase(ApiErrorKind.ServiceOutage, "AniList is Down")]
    [TestCase(ApiErrorKind.Network, "No Internet Connection")]
    [TestCase(ApiErrorKind.Authentication, "Session Expired")]
    [TestCase(ApiErrorKind.RateLimited, "Slow Down a Sec")]
    [TestCase(ApiErrorKind.NotFound, "Entry Unavailable")]
    [TestCase(ApiErrorKind.Unknown, "Something Went Wrong")]
    public void AniListApiException_EveryKind_HasFriendlyTitleAndNonEmptySubtitle(ApiErrorKind kind, string expectedTitle)
    {
        var ex = new AniListApiException(kind, "raw message");

        // Every kind must map to a curated title (not leak the raw message) and offer guidance.
        Assert.That(ex.UserTitle, Is.EqualTo(expectedTitle));
        Assert.That(string.IsNullOrWhiteSpace(ex.UserSubtitle), Is.False);
    }
}
