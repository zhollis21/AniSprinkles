using System.Net;
using System.Net.Http.Headers;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniSprinkles.UnitTests;

public class AniListRateLimitHandlerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

    private static HttpResponseMessage TooMany(int retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
        return response;
    }

    private static HttpRequestMessage Request() =>
        new(HttpMethod.Post, "https://graphql.anilist.co") { Content = new StringContent("{\"query\":\"x\"}") };

    private static AniListRateLimitHandler CreateHandler(
        HttpMessageHandler inner,
        ManualTimeProvider time,
        int maxRetries = 3,
        TimeSpan? maxAutoRetryWait = null)
        => new(
            time,
            NullLogger<AniListRateLimitHandler>.Instance,
            maxRetries: maxRetries,
            maxAutoRetryWait: maxAutoRetryWait ?? TimeSpan.FromSeconds(5))
        {
            InnerHandler = inner,
        };

    private static async Task AdvanceUntilCompleteAsync(Task task, ManualTimeProvider time, int maxSteps = 50)
    {
        for (var i = 0; i < maxSteps && !task.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }
    }

    [Fact]
    public async Task SendAsync_CarriesRequestOptionsThroughTheRetryTemplate()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => Ok());
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        using var callerCts = new CancellationTokenSource();
        var request = Request();
        request.Options.Set(LoggingHandler.CallerCancellationToken, callerCts.Token);

        await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        // Every send goes out as a rebuilt RequestTemplate, not the caller's message — that is what
        // lets a 429 be retried, since a request and its content stream can only be sent once. So
        // anything recorded on Options has to be copied across, or it silently never reaches the
        // handlers below. LoggingHandler's caller-token lookup is the first thing that depends on it.
        var sent = inner.LastRequest;
        Assert.NotNull(sent);
        Assert.True(sent.Options.TryGetValue(LoggingHandler.CallerCancellationToken, out var carried));
        Assert.Equal(callerCts.Token, carried);
    }

    [Fact]
    public async Task SendAsync_SuccessfulResponse_PassesThroughUnchanged()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => Ok());
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        var response = await invoker.SendAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_TooManyRequestsThenSuccess_WaitsRetryAfterThenRetries()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(i => i == 0 ? TooMany(1) : Ok());
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        var task = invoker.SendAsync(Request(), TestContext.Current.CancellationToken);
        await AdvanceUntilCompleteAsync(task, time);
        var response = await task;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_RetryAfterExceedsCap_SurfacesRateLimitedImmediately()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => TooMany(30)); // > 5s cap
        using var invoker = new HttpMessageInvoker(
            CreateHandler(inner, time, maxAutoRetryWait: TimeSpan.FromSeconds(5)));

        var ex = await Assert.ThrowsAsync<AniListApiException>(() => invoker.SendAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(ApiErrorKind.RateLimited, ex.Kind);
        Assert.Equal(1, inner.CallCount); // no retry attempted
    }

    [Fact]
    public async Task SendAsync_PersistentTooManyRequests_ThrowsRateLimitedAfterMaxRetries()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => TooMany(1));
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time, maxRetries: 2));

        var task = invoker.SendAsync(Request(), TestContext.Current.CancellationToken);
        await AdvanceUntilCompleteAsync(task, time);

        var ex = await Assert.ThrowsAsync<AniListApiException>(() => task);
        Assert.Equal(ApiErrorKind.RateLimited, ex.Kind);
        Assert.Equal(3, inner.CallCount); // initial + 2 retries
    }

    [Fact]
    public async Task SendAsync_ConcurrentRequests_AreSerialized()
    {
        var time = new ManualTimeProvider(Start);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new QueuedHttpMessageHandler(i => i == 0 ? gate.Task : Task.FromResult(Ok()));
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        var first = invoker.SendAsync(Request(), TestContext.Current.CancellationToken);
        var second = invoker.SendAsync(Request(), TestContext.Current.CancellationToken);

        // Let the second request progress as far as it can — it must block on the gate's semaphore,
        // so the inner handler is only ever entered once while the first is held open.
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }

        Assert.Equal(1, inner.CallCount);

        gate.SetResult(Ok());
        await Task.WhenAll(first, second);

        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task SendAsync_CancelledDuringRetryDelay_ThrowsOperationCanceled()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => TooMany(2));
        using var cts = new CancellationTokenSource();
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        var task = invoker.SendAsync(Request(), cts.Token);
        // First attempt has happened (429); handler is now waiting out Retry-After.
        await Task.Yield();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, inner.CallCount);
    }
}
