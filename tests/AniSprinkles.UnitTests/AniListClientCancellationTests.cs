using System.Net;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The line between "the user navigated away" and "the request timed out". Both surface as a
/// <see cref="TaskCanceledException"/> from <c>HttpClient</c>, and the details page models swallow
/// cancellation silently — so if a timeout reached them as one, the page would sit on a spinner
/// forever with no error and no retry. <c>AniListClient</c> is what keeps those apart, by classifying
/// a timeout as <see cref="ApiErrorKind.Network"/> before it ever leaves the client.
/// </summary>
public class AniListClientCancellationTests
{
    [Fact]
    public async Task SendAsync_WhenTheRequestTimesOut_SurfacesAsANetworkErrorRatherThanACancellation()
    {
        // What HttpClient throws on its own timeout: a TaskCanceledException wrapping a TimeoutException.
        var handler = new QueuedHttpMessageHandler(
            _ => Task.FromException<HttpResponseMessage>(
                new TaskCanceledException("timed out", new TimeoutException())));

        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ApiErrorKind.Network, ex.Kind);
        Assert.IsNotType<OperationCanceledException>(ex, exactMatch: false);
    }

    [Fact]
    public async Task SendAsync_WhenTheCallerCancels_StaysACancellation()
    {
        using var cts = new CancellationTokenSource();
        var handler = new QueuedHttpMessageHandler(
            _ => Task.FromException<HttpResponseMessage>(new TaskCanceledException("navigated away")));

        var client = NewClient(handler);
        await cts.CancelAsync();

        // A cancellation with no inner TimeoutException is the navigate-away case, and must keep its
        // type so the page models' catch (OperationCanceledException) can abandon quietly.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetCharacterAsync(1, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task SendAsync_WhenAndroidReportsTheCancellationAsAClosedSocket_StaysACancellation()
    {
        using var cts = new CancellationTokenSource();

        // Android does not report a cancelled request as a cancellation. AndroidMessageHandler
        // disconnects the underlying HttpURLConnection, so the read already in flight fails with
        // Java.Net.SocketException("Socket closed") wrapped in a WebException. Observed in
        // production off a debounced search the user typed straight through.
        var handler = new QueuedHttpMessageHandler(
            _ => Task.FromException<HttpResponseMessage>(
                new WebException("Socket closed", new IOException("Socket closed"))));

        var client = NewClient(handler);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetCharacterAsync(1, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task SendAsync_WhenTheSocketDropsWithNoCancellation_SurfacesAsANetworkError()
    {
        // Same exception type, nobody cancelled: a genuinely dropped connection, which the caller
        // must see as a retryable network failure rather than as a raw WebException.
        var handler = new QueuedHttpMessageHandler(
            _ => Task.FromException<HttpResponseMessage>(
                new WebException("Socket closed", new IOException("Socket closed"))));

        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ApiErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task SendAsync_WhenTheConnectionFails_SurfacesAsANetworkError()
    {
        var handler = new QueuedHttpMessageHandler(
            _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("no route to host")));

        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => client.GetCharacterAsync(1, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ApiErrorKind.Network, ex.Kind);
    }

    private static AniListClient NewClient(HttpMessageHandler handler)
    {
        var auth = Substitute.For<IAuthService>();
        auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

        return new AniListClient(
            new HttpClient(handler),
            auth,
            Substitute.For<IOutageStateService>(),
            NullLogger<AniListClient>.Instance);
    }
}
