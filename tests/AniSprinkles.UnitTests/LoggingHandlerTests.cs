using System.Net;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.UnitTests;

/// <summary>
/// <see cref="LoggingHandler"/> decides, for every failed request, whether it becomes a Sentry
/// event or a breadcrumb — Sentry's ILogger integration captures Error and only records
/// Information. The distinction that matters is "the app abandoned this request" vs "this request
/// failed", and on Android the exception type does not draw that line: cancelling closes the socket
/// under the in-flight read, so an abandoned request arrives as WebException("Socket closed") and
/// not as an OperationCanceledException. Only the token knows.
/// </summary>
public class LoggingHandlerTests
{
    [Fact]
    public async Task SendAsync_WhenTheCallerCancelled_LogsTheFailureAsInformationRatherThanError()
    {
        var logger = new RecordingLogger<LoggingHandler>();
        using var invoker = NewInvoker(logger, new WebException("Socket closed", new IOException("Socket closed")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<WebException>(
            () => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://graphql.anilist.co/"), cts.Token));

        var failure = Assert.Single(logger.Entries, e => e.Message.Contains("cancelled", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, failure.Level);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task SendAsync_WhenNobodyCancelled_StillLogsTheFailureAsError()
    {
        var logger = new RecordingLogger<LoggingHandler>();
        using var invoker = NewInvoker(logger, new WebException("Socket closed", new IOException("Socket closed")));

        await Assert.ThrowsAsync<WebException>(
            () => invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "https://graphql.anilist.co/"),
                TestContext.Current.CancellationToken));

        var failure = Assert.Single(logger.Entries, e => e.Message.Contains("failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, failure.Level);
    }

    private static HttpMessageInvoker NewInvoker(ILogger<LoggingHandler> logger, Exception failure)
        => new(new LoggingHandler(logger)
        {
            InnerHandler = new QueuedHttpMessageHandler(_ => Task.FromException<HttpResponseMessage>(failure))
        });
}
