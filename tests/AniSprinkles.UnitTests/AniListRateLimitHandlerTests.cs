using System.Net;
using System.Net.Http.Headers;
using AniSprinkles.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AniSprinkles.UnitTests;

public class AniListRateLimitHandlerTests
{
    [Fact]
    public async Task SendAsync_RemainingAboveThreshold_DoesNotLogWarning()
    {
        var logger = Substitute.For<ILogger<AniListRateLimitHandler>>();
        var response = BuildResponse(HttpStatusCode.OK, remaining: "50", limit: "90");
        var client = BuildClient(logger, response);

        using var result = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://graphql.anilist.co/"));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        AssertNoLog(logger, LogLevel.Warning);
    }

    [Fact]
    public async Task SendAsync_RemainingBelowThreshold_LogsWarning()
    {
        var logger = Substitute.For<ILogger<AniListRateLimitHandler>>();
        var response = BuildResponse(HttpStatusCode.OK, remaining: "3", limit: "90");
        var client = BuildClient(logger, response);

        using var result = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://graphql.anilist.co/"));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        AssertWarningLogged(logger);
    }

    [Fact]
    public async Task SendAsync_TooManyRequests_LogsWarning_WithRetryAfterSeconds()
    {
        var logger = Substitute.For<ILogger<AniListRateLimitHandler>>();
        var response = BuildResponse(HttpStatusCode.TooManyRequests, remaining: "0", limit: "90");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
        var client = BuildClient(logger, response);

        using var result = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://graphql.anilist.co/"));

        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        AssertWarningLogged(logger);
    }

    [Fact]
    public async Task SendAsync_OkWithoutRateLimitHeaders_DoesNotLog()
    {
        var logger = Substitute.For<ILogger<AniListRateLimitHandler>>();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var client = BuildClient(logger, response);

        using var result = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://graphql.anilist.co/"));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        AssertNoLog(logger, LogLevel.Warning);
    }

    private static HttpResponseMessage BuildResponse(HttpStatusCode status, string remaining, string limit)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Add("X-RateLimit-Remaining", remaining);
        response.Headers.Add("X-RateLimit-Limit", limit);
        return response;
    }

    private static HttpClient BuildClient(ILogger<AniListRateLimitHandler> logger, HttpResponseMessage cannedResponse)
    {
        var rateLimit = new AniListRateLimitHandler(logger)
        {
            InnerHandler = new StubHandler(cannedResponse)
        };
        return new HttpClient(rateLimit);
    }

    private static void AssertWarningLogged(ILogger<AniListRateLimitHandler> logger)
    {
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    private static void AssertNoLog(ILogger<AniListRateLimitHandler> logger, LogLevel level)
    {
        logger.DidNotReceive().Log(
            level,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }
}
