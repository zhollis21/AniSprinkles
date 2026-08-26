using System.Net;
using AniSprinkles.Services.FaultInjection;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniSprinkles.UnitTests;

/// <summary>
/// Covers the HTTP seam (#125): that a synthetic failure is shaped closely enough to a real AniList
/// one that the pipeline above it behaves identically.
/// <para>
/// The last test is the one that justifies the seam existing at all — it puts the fault handler
/// underneath the real <c>AniListRateLimitHandler</c> and shows the handler absorbing an injected
/// 429 and retrying, which is behaviour the client-level decorator sits above and can never reach.
/// </para>
/// </summary>
public class FaultInjectingHttpHandlerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static HttpRequestMessage Request(string operationName = "Media")
        => new(HttpMethod.Post, "https://graphql.anilist.co")
        {
            Content = new StringContent($"{{\"query\":\"x\",\"operationName\":\"{operationName}\"}}"),
        };

    private static FaultInjectingHttpHandler Create(FaultState state, HttpMessageHandler inner)
        => new(state, NullLogger<FaultInjectingHttpHandler>.Instance) { InnerHandler = inner };

    private static FaultProfile Http(
        ApiErrorKind? kind, FaultScope scope, string? op = null, TimeSpan delay = default, bool graphQl = false)
        => new(op, kind, scope, delay, FaultLayer.Http, graphQl);

    [Fact]
    public async Task Disarmed_PassesTheRequestThrough()
    {
        var state = new FaultState();
        var inner = new QueuedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(Create(state, inner));

        var response = await client.SendAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task ClientLayerProfile_DoesNotFireInTheHttpPipeline()
    {
        var state = new FaultState();
        state.Arm(new FaultProfile(null, ApiErrorKind.ServiceOutage, FaultScope.Always, default, FaultLayer.Client));
        var inner = new QueuedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(Create(state, inner));

        var response = await client.SendAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Theory]
    [InlineData(ApiErrorKind.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(ApiErrorKind.ServiceOutage, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ApiErrorKind.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ApiErrorKind.Unknown, HttpStatusCode.InternalServerError)]
    public async Task ArmedFault_AnswersWithTheMatchingStatusAndNeverHitsTheNetwork(
        ApiErrorKind kind, HttpStatusCode expected)
    {
        var state = new FaultState();
        state.Arm(Http(kind, FaultScope.Always));
        var inner = new QueuedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(Create(state, inner));

        var response = await client.SendAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task AuthenticationFault_UsesAniListsActualShape()
    {
        // AniList returns 400 with "Invalid token", not 401 — AniListErrorClassifier keys off the
        // message for exactly this reason, so a synthetic 401 would test the wrong branch.
        var state = new FaultState();
        state.Arm(Http(ApiErrorKind.Authentication, FaultScope.Always));
        using var client = new HttpClient(Create(state, new QueuedHttpMessageHandler(_ => new HttpResponseMessage())));

        var response = await client.SendAsync(Request(), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Invalid token", body, StringComparison.Ordinal);
        Assert.Equal(
            ApiErrorKind.Authentication,
            AniListErrorClassifier.ClassifyHttpError(response.StatusCode, "Invalid token"));
    }

    [Fact]
    public async Task GraphQlErrorFault_AnswersHttp200WithAnErrorsArray()
    {
        // The branch that routes through ClassifyGraphQlError rather than ClassifyHttpError. Nothing
        // could reach it on device before this seam existed.
        var state = new FaultState();
        state.Arm(Http(ApiErrorKind.NotFound, FaultScope.Always, graphQl: true));
        using var client = new HttpClient(Create(state, new QueuedHttpMessageHandler(_ => new HttpResponseMessage())));

        var response = await client.SendAsync(Request(), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"errors\"", body, StringComparison.Ordinal);
        Assert.Equal(ApiErrorKind.NotFound, AniListErrorClassifier.ClassifyGraphQlError("Not Found."));
    }

    [Fact]
    public async Task NetworkFault_ThrowsSoTheClientClassifiesItAsNetwork()
    {
        var state = new FaultState();
        state.Arm(Http(ApiErrorKind.Network, FaultScope.Always));
        using var client = new HttpClient(Create(state, new QueuedHttpMessageHandler(_ => new HttpResponseMessage())));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OperationPrefix_TargetsASingleQuery()
    {
        var state = new FaultState();
        state.Arm(Http(ApiErrorKind.NotFound, FaultScope.Always, op: "Staff"));
        var inner = new QueuedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(Create(state, inner));

        var untargeted = await client.SendAsync(Request("Media"), TestContext.Current.CancellationToken);
        var targeted = await client.SendAsync(Request("Staff"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, untargeted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, targeted.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task AnIAniListClientMethodName_DoesNotMatchAtTheHttpLayer()
    {
        // The layers key off different names: this seam sees the GraphQL operationName, the decorator
        // sees the C# method name. `fault GetStudio ... -layer http` therefore fires nothing, which is
        // indistinguishable from a broken seam unless you know — hence the no-match log line and the
        // documented rule. Pinned here so nobody "fixes" the mismatch by normalising one side.
        var state = new FaultState();
        state.Arm(Http(ApiErrorKind.NotFound, FaultScope.Always, op: "GetStudio"));
        var inner = new QueuedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(Create(state, inner));

        var response = await client.SendAsync(Request("Studio"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task TheWireNameMatchesWhereTheMethodNameDoesNot()
    {
        // The same operation, targeted the way the http layer actually spells it.
        var state = new FaultState();
        state.Arm(Http(ApiErrorKind.NotFound, FaultScope.Always, op: "Studio"));
        var inner = new QueuedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(Create(state, inner));

        var response = await client.SendAsync(Request("Studio"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task InjectedRateLimit_IsAbsorbedAndRetriedByTheRealRateLimitHandler()
    {
        // The reason this seam exists. `scope next` spends the 429 on the first attempt, so the
        // rate-limit handler waits out Retry-After and the retry reaches the network and succeeds —
        // rehearsing the adaptive-spacing path without spending real budget against AniList.
        var time = new ManualTimeProvider(Start);
        var state = new FaultState();
        state.Arm(Http(ApiErrorKind.RateLimited, FaultScope.Next, delay: TimeSpan.FromSeconds(2)));

        var network = new QueuedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var fault = Create(state, network);
        var rateLimit = new AniListRateLimitHandler(time, NullLogger<AniListRateLimitHandler>.Instance)
        {
            InnerHandler = fault,
        };
        using var client = new HttpClient(rateLimit);

        var pending = client.SendAsync(Request(), TestContext.Current.CancellationToken);
        for (var i = 0; i < 50 && !pending.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        var response = await pending;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The first attempt was answered synthetically; only the retry reached the network.
        Assert.Equal(1, network.CallCount);
    }
}
