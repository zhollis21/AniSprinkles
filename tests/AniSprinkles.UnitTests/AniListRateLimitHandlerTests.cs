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

    [Test]
    public async Task SendAsync_CarriesRequestOptionsThroughTheRetryTemplate()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => Ok());
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        using var callerCts = new CancellationTokenSource();
        var request = Request();
        request.Options.Set(LoggingHandler.CallerCancellationToken, callerCts.Token);

        await invoker.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        // Every send goes out as a rebuilt RequestTemplate, not the caller's message — that is what
        // lets a 429 be retried, since a request and its content stream can only be sent once. So
        // anything recorded on Options has to be copied across, or it silently never reaches the
        // handlers below. LoggingHandler's caller-token lookup is the first thing that depends on it.
        var sent = inner.LastRequest;
        Assert.That(sent, Is.Not.Null);
        Assert.That(sent.Options.TryGetValue(LoggingHandler.CallerCancellationToken, out var carried), Is.True);
        Assert.That(carried, Is.EqualTo(callerCts.Token));
    }

    [Test]
    public async Task SendAsync_CarriesRequestHeadersThroughTheRetryTemplate()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => Ok());
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        var request = Request();
        request.Headers.Referrer = new Uri("https://anilist.co/");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token-abc");

        await invoker.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        // Same rebuild hazard as the Options test above, but the cost of losing these is remote
        // rather than local: AniList answers a request carrying neither header with a 403 whose body
        // claims the API is down (#160). A template that dropped them would turn every retry into a
        // fake outage, and the retry path is exactly where nobody looks.
        var sent = inner.LastRequest;
        Assert.That(sent, Is.Not.Null);
        Assert.That(sent.Headers.Referrer?.ToString(), Is.EqualTo("https://anilist.co/"));
        Assert.That(sent.Headers.Authorization?.Parameter, Is.EqualTo("token-abc"));
    }

    [Test]
    public async Task SendAsync_SuccessfulResponse_PassesThroughUnchanged()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => Ok());
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        var response = await invoker.SendAsync(Request(), TestContext.CurrentContext.CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(inner.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SendAsync_TooManyRequestsThenSuccess_WaitsRetryAfterThenRetries()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(i => i == 0 ? TooMany(1) : Ok());
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        var task = invoker.SendAsync(Request(), TestContext.CurrentContext.CancellationToken);
        await time.AdvanceUntilCompleteAsync(task, TimeSpan.FromSeconds(1));
        var response = await task;

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(inner.CallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SendAsync_RetryAfterExceedsCap_SurfacesRateLimitedImmediately()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => TooMany(30)); // > 5s cap
        using var invoker = new HttpMessageInvoker(
            CreateHandler(inner, time, maxAutoRetryWait: TimeSpan.FromSeconds(5)));

        var ex = await Assert.ThrowsAsync<AniListApiException>(() => invoker.SendAsync(Request(), TestContext.CurrentContext.CancellationToken));

        Assert.That(ex.Kind, Is.EqualTo(ApiErrorKind.RateLimited));
        Assert.That(inner.CallCount, Is.EqualTo(1)); // no retry attempted
    }

    [Test]
    public async Task SendAsync_PersistentTooManyRequests_ThrowsRateLimitedAfterMaxRetries()
    {
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(_ => TooMany(1));
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time, maxRetries: 2));

        var task = invoker.SendAsync(Request(), TestContext.CurrentContext.CancellationToken);
        await time.AdvanceUntilCompleteAsync(task, TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<AniListApiException>(() => task);
        Assert.That(ex.Kind, Is.EqualTo(ApiErrorKind.RateLimited));
        Assert.That(inner.CallCount, Is.EqualTo(3)); // initial + 2 retries
    }

    [Test]
    [CancelAfter(10_000)]
    public async Task SendAsync_InnerHandlerSlowerThanTheTestLoop_StillRetriesToCompletion()
    {
        // The CI hang (#178): on a loaded runner the handler reached its retry delay only after
        // the test had finished advancing the fake clock, so the delay's timer was never fired.
        // A response that takes real time to arrive reproduces that ordering on any machine.
        var time = new ManualTimeProvider(Start);
        var inner = new QueuedHttpMessageHandler(async i =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            return i < 2 ? TooMany(1) : Ok();
        });
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time, maxRetries: 2));

        var task = invoker.SendAsync(Request(), TestContext.CurrentContext.CancellationToken);
        await time.AdvanceUntilCompleteAsync(task, TimeSpan.FromSeconds(1));
        var response = await task;

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(inner.CallCount, Is.EqualTo(3));
    }

    [Test]
    public async Task SendAsync_ConcurrentRequests_AreSerialized()
    {
        var time = new ManualTimeProvider(Start);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new QueuedHttpMessageHandler(i => i == 0 ? gate.Task : Task.FromResult(Ok()));
        using var invoker = new HttpMessageInvoker(CreateHandler(inner, time));

        var first = invoker.SendAsync(Request(), TestContext.CurrentContext.CancellationToken);
        var second = invoker.SendAsync(Request(), TestContext.CurrentContext.CancellationToken);

        // Let the second request progress as far as it can — it must block on the gate's semaphore,
        // so the inner handler is only ever entered once while the first is held open.
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }

        Assert.That(inner.CallCount, Is.EqualTo(1));

        gate.SetResult(Ok());
        await Task.WhenAll(first, second);

        Assert.That(inner.CallCount, Is.EqualTo(2));
    }

    [Test]
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

        await Assert.CatchAsync<OperationCanceledException>(() => task);
        Assert.That(inner.CallCount, Is.EqualTo(1));
    }
}
