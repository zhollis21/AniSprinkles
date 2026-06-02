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
    [InlineData("Invalid token provided", ApiErrorKind.Authentication)]
    [InlineData("Unauthorized", ApiErrorKind.Authentication)]
    [InlineData("AniList is under maintenance", ApiErrorKind.ServiceOutage)]
    [InlineData("Not Found.", ApiErrorKind.NotFound)]
    [InlineData("Validation error: bad field", ApiErrorKind.Unknown)]
    public void ClassifyGraphQlError_ByMessage_MapsToExpectedKind(string message, ApiErrorKind expected)
    {
        Assert.Equal(expected, AniListErrorClassifier.ClassifyGraphQlError(message));
    }

    [Fact]
    public void AniListApiException_RateLimited_HasFriendlyTitleAndSubtitle()
    {
        var ex = new AniListApiException(ApiErrorKind.RateLimited, "rate limited");

        Assert.Equal("Slow Down a Sec", ex.UserTitle);
        Assert.False(string.IsNullOrWhiteSpace(ex.UserSubtitle));
    }

    [Fact]
    public void AniListApiException_NotFound_HasFriendlyTitleAndSubtitle()
    {
        var ex = new AniListApiException(ApiErrorKind.NotFound, "Not Found.");

        Assert.Equal("Title Unavailable", ex.UserTitle);
        Assert.False(string.IsNullOrWhiteSpace(ex.UserSubtitle));
    }
}
