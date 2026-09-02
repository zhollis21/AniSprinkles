using System.Net;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.UnitTests;

/// <summary>
/// <see cref="LoggingHandler"/> decides, for every failed request, whether it becomes a Sentry
/// event or a breadcrumb — Sentry's ILogger integration captures Error and only records
/// Information. The distinction that matters is "the app abandoned this request" vs "this request
/// failed", and inside the pipeline neither the exception nor the token can draw it:
/// <list type="bullet">
/// <item>The exception can't: on Android, cancelling closes the socket under the in-flight read, so
/// an abandoned request arrives as WebException("Socket closed") rather than an
/// OperationCanceledException — and an <see cref="HttpClient"/> timeout cancels the same way, so it
/// arrives wearing the same clothes.</item>
/// <item>The pipeline token can't either: it is HttpClient's *linked* token, cancelled both when
/// the caller cancels and when HttpClient's own timeout fires.</item>
/// </list>
/// So the caller records its own token on the request, and that is what these tests pin.
/// </summary>
public class LoggingHandlerTests
{
    [Fact]
    public async Task SendAsync_WhenTheCallerCancelled_LogsTheFailureAsInformationRatherThanError()
    {
        var logger = new RecordingLogger<LoggingHandler>();
        using var invoker = NewInvoker(logger, SocketClosed());
        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        await Assert.ThrowsAsync<WebException>(
            () => invoker.SendAsync(NewRequest(callerCts.Token), callerCts.Token));

        var failure = Assert.Single(logger.Entries, e => e.Message.Contains("cancelled", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, failure.Level);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task SendAsync_WhenOnlyTheTimeoutCancelledThePipeline_StillLogsAsError()
    {
        // What an HttpClient.Timeout looks like from inside the pipeline: the linked token is
        // cancelled, but the caller's own token is not. On Android the timeout tears the socket down
        // the same way a cancellation does, so the exception is identical to the case above — the
        // caller's token is the only thing that differs, and a real timeout must still be reported.
        var logger = new RecordingLogger<LoggingHandler>();
        using var invoker = NewInvoker(logger, SocketClosed());
        using var timeoutCts = new CancellationTokenSource();
        await timeoutCts.CancelAsync();

        await Assert.ThrowsAsync<WebException>(
            () => invoker.SendAsync(NewRequest(CancellationToken.None), timeoutCts.Token));

        var failure = Assert.Single(logger.Entries, e => e.Message.Contains("failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, failure.Level);
    }

    [Fact]
    public async Task SendAsync_WhenNoCallerTokenWasRecorded_LogsAsError()
    {
        // An unknown caller gets the reporting default. Missing a cancellation costs one Sentry
        // event; missing a real failure costs the report that would have explained an outage.
        var logger = new RecordingLogger<LoggingHandler>();
        using var invoker = NewInvoker(logger, SocketClosed());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<WebException>(
            () => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, Endpoint), cts.Token));

        var failure = Assert.Single(logger.Entries, e => e.Message.Contains("failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, failure.Level);
    }

    [Fact]
    public async Task SendAsync_WhenNobodyCancelled_StillLogsTheFailureAsError()
    {
        var logger = new RecordingLogger<LoggingHandler>();
        using var invoker = NewInvoker(logger, SocketClosed());

        await Assert.ThrowsAsync<WebException>(
            () => invoker.SendAsync(NewRequest(CancellationToken.None), TestContext.Current.CancellationToken));

        var failure = Assert.Single(logger.Entries, e => e.Message.Contains("failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, failure.Level);
    }

    private const string Endpoint = "https://graphql.anilist.co/";

    private static WebException SocketClosed() => new("Socket closed", new IOException("Socket closed"));

    private static HttpRequestMessage NewRequest(CancellationToken callerToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Options.Set(LoggingHandler.CallerCancellationToken, callerToken);
        return request;
    }

    private static HttpMessageInvoker NewInvoker(ILogger<LoggingHandler> logger, Exception failure)
        => new(new LoggingHandler(logger)
        {
            InnerHandler = new QueuedHttpMessageHandler(_ => Task.FromException<HttpResponseMessage>(failure))
        });
}
