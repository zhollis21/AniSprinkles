using System.Net;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 2 for <see cref="AniListClient"/> — the shared plumbing every one of its two dozen
/// operations funnels through: the request envelope, the bearer header, error classification,
/// redaction, outage reporting, and the single transient retry.
/// <para>
/// Per-operation request shapes and deserialization live in
/// <see cref="AniListClientOperationsTests"/>. Cancellation-vs-timeout has its own file
/// (<see cref="AniListClientCancellationTests"/>) and is not repeated here.
/// </para>
/// </summary>
public class AniListClientTests
{
    private const string SomeMedia = """{"Media":{"id":1,"type":"ANIME"}}""";

    // ── The request envelope ─────────────────────────────────────────

    [Fact]
    public async Task EveryRequest_IsAPostToTheGraphQlEndpoint()
    {
        var harness = new Harness(SomeMedia);

        await harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, harness.Handler.Last.Method);
        Assert.Equal("https://graphql.anilist.co/", harness.Handler.Last.Uri?.ToString());
    }

    [Fact]
    public async Task ARequestCarriesItsQueryVariablesAndOperationName()
    {
        var harness = new Harness(SomeMedia);

        await harness.Client.GetMediaAsync(77, TestContext.Current.CancellationToken);

        var request = harness.Handler.Last;
        Assert.Equal("Media", request.OperationName);
        Assert.NotNull(request.Query);
        // The operation name has to match a named operation in the document, or AniList rejects it.
        Assert.Contains($"query {request.OperationName}", request.Query, StringComparison.Ordinal);
        Assert.Equal(77, request.IntVariable("id"));
    }

    // ── #160: AniList rejects requests that carry neither a Referer nor a token ──

    [Fact]
    public async Task EveryRequest_CarriesTheAniListReferer()
    {
        // AniList answers a request with no Referer and no bearer token with HTTP 403 and a body
        // claiming the API is "temporarily disabled" — which AniListErrorClassifier then reads as a
        // service outage. Confirmed on device 2026-09-05: the whole app is dead signed out, and
        // details-page sorts fail signed in, both behind a false "AniList is Down" banner (#160).
        var harness = new Harness(SomeMedia);

        await harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal("https://anilist.co/", harness.Handler.Last.Referrer?.ToString());
    }

    [Fact]
    public async Task ASignedOutRequest_StillCarriesTheReferer()
    {
        // The case that matters most: signed out there is no token to fall back on, so the Referer
        // is the only thing keeping Discover, Search and the details pages reachable at all.
        var harness = new Harness(SomeMedia);
        harness.SignOut();

        await harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken);

        Assert.Null(harness.Handler.Last.BearerToken);
        Assert.Equal("https://anilist.co/", harness.Handler.Last.Referrer?.ToString());
    }

    [Theory]
    [InlineData("MediaCharactersPage")]
    [InlineData("MediaStaffPage")]
    [InlineData("StaffCharactersPage")]
    public async Task PublicPagingOperations_SendTheTokenWhenThereIsOne(string operationName)
    {
        // These three used to pass token: null on the grounds that the query is public. It is, but
        // withholding a token we already hold bought nothing and cost the only other thing that
        // satisfies AniList's filter. Signed in, they should look like every other request.
        var harness = new Harness("""
            {"Media":{"characters":{"pageInfo":{"currentPage":1,"hasNextPage":false},"edges":[]},
             "staff":{"pageInfo":{"currentPage":1,"hasNextPage":false},"edges":[]}},
             "Staff":{"characters":{"pageInfo":{"currentPage":1,"hasNextPage":false},"edges":[]}}}
            """);

        var ct = TestContext.Current.CancellationToken;
        switch (operationName)
        {
            case "MediaCharactersPage":
                await harness.Client.LoadMediaCharactersPageAsync(1, 1, "ROLE", cancellationToken: ct);
                break;
            case "MediaStaffPage":
                await harness.Client.LoadMediaStaffPageAsync(1, 1, "RELEVANCE", cancellationToken: ct);
                break;
            default:
                await harness.Client.LoadStaffCharactersPageAsync(1, 1, "FAVOURITES_DESC", cancellationToken: ct);
                break;
        }

        var request = harness.Handler.Last;
        Assert.Equal(operationName, request.OperationName);
        Assert.Equal("token-abc", request.BearerToken);
    }

    [Fact]
    public async Task TheAiringSchedule_SendsTheTokenWhenThereIsOne()
    {
        // The airing worker can run with no token at all after a reboot (#149), which is why the
        // Referer above is the load-bearing half of the fix — but when a token exists, send it.
        var harness = new Harness("""
            {"Page":{"pageInfo":{"currentPage":1,"hasNextPage":false},"airingSchedules":[]}}
            """);

        await harness.Client.GetAiringScheduleAsync([1], 0, 1, TestContext.Current.CancellationToken);

        Assert.Equal("token-abc", harness.Handler.Last.BearerToken);
    }

    [Fact]
    public async Task AnOperationWithNoVariables_SendsNone()
    {
        // Viewer takes no arguments; sending "variables": null is fine, an empty object is not the
        // point — what matters is that nothing invents arguments for it.
        var harness = new Harness("""{"Viewer":{"id":5}}""");

        await harness.Client.GetCurrentUserIdAsync(TestContext.Current.CancellationToken);

        Assert.Null(harness.Handler.Last.Variables);
    }

    [Fact]
    public async Task NullFilters_AreOmittedRatherThanSentAsNull()
    {
        // The distinction AniList cares about: an omitted argument matches everything, a literal
        // null filters for null. Sending status: null would quietly return an empty browse list.
        var harness = new Harness("""{"Page":{"pageInfo":{"currentPage":1,"hasNextPage":false},"media":[]}}""");

        await harness.Client.BrowseAnimePageAsync(
            "POPULARITY_DESC", cancellationToken: TestContext.Current.CancellationToken);

        var request = harness.Handler.Last;
        Assert.True(request.HasVariable("page"));
        Assert.False(request.HasVariable("status"));
        Assert.False(request.HasVariable("season"));
        Assert.False(request.HasVariable("isAdult"));
        Assert.False(request.HasVariable("format"));
    }

    // ── Authentication ───────────────────────────────────────────────

    [Fact]
    public async Task WhenSignedIn_TheBearerTokenIsAttached()
    {
        var harness = new Harness(SomeMedia);

        await harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", harness.Handler.Last.AuthScheme);
        Assert.Equal("token-abc", harness.Handler.Last.BearerToken);
    }

    [Fact]
    public async Task WhenSignedOut_APublicQueryStillGoesOutWithNoAuthHeader()
    {
        // Browse and media details are public; requiring a token would break the signed-out app.
        var harness = new Harness(SomeMedia);
        harness.SignOut();

        await harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken);

        Assert.Null(harness.Handler.Last.AuthScheme);
        Assert.Equal(1, harness.Handler.CallCount);
    }

    [Fact]
    public async Task WhenSignedOut_AMutationFailsBeforeAnythingIsSent()
    {
        // No point spending a request (or a rate-limit slot) on a call that cannot succeed.
        var harness = new Harness(SomeMedia);
        harness.SignOut();

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.DeleteMediaListEntryAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal(ApiErrorKind.Authentication, ex.Kind);
        Assert.Equal(0, harness.Handler.CallCount);
    }

    // ── Error surfaces ───────────────────────────────────────────────

    [Fact]
    public async Task GraphQlErrorsOnASuccessfulHttpStatus_StillFail()
    {
        // AniList answers 200 with an errors array; treating that as success would hand the page
        // models an empty result and no reason for it.
        var harness = new Harness(_ => ScriptedGraphQlHandler.GraphQlError("Not Found."));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal("Not Found.", ex.Message);
    }

    [Fact]
    public async Task AnHttpFailureCarryingAGraphQlBody_SurfacesTheApiMessage()
    {
        var harness = new Harness(_ => ScriptedGraphQlHandler.GraphQlError(
            "Too Many Requests.", HttpStatusCode.TooManyRequests));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal("Too Many Requests.", ex.Message);
    }

    [Fact]
    public async Task AnHttpFailureWithAnUnreadableBody_StillReportsTheStatus()
    {
        var harness = new Harness(_ => ScriptedGraphQlHandler.Raw(
            HttpStatusCode.BadGateway, "<html>gateway blew up</html>"));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task AResponseWithNeitherDataNorErrors_IsAnError()
    {
        var harness = new Harness(_ => ScriptedGraphQlHandler.Raw(HttpStatusCode.OK, "{}"));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal(ApiErrorKind.Unknown, ex.Kind);
    }

    [Fact]
    public async Task ACredentialEchoedBackInAnError_IsRedactedBeforeItIsStored()
    {
        // #124. The exception message reaches the file log, logcat and Sentry, and an auth failure
        // is exactly the response most likely to quote the credential back at us.
        var harness = new Harness(_ => ScriptedGraphQlHandler.GraphQlError(
            "Invalid token: Bearer sk-secret-value-12345", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        Assert.DoesNotContain("sk-secret-value-12345", ex.Message);
    }

    [Fact]
    public async Task RedactionDoesNotCostTheErrorItsClassification()
    {
        // Classification reads the raw message and redaction rewrites it, so doing them in the
        // wrong order would downgrade a known auth failure to Unknown.
        var harness = new Harness(_ => ScriptedGraphQlHandler.GraphQlError(
            "Invalid token: Bearer sk-secret-value-12345", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal(ApiErrorKind.Authentication, ex.Kind);
    }

    // ── Outage reporting ─────────────────────────────────────────────

    [Fact]
    public async Task ASuccessfulCall_ClearsAnyOutage()
    {
        var harness = new Harness(SomeMedia);

        await harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken);

        harness.Outage.Received(1).ReportSuccess();
        harness.Outage.DidNotReceive().ReportFailure(Arg.Any<Exception>());
    }

    [Fact]
    public async Task AFailedCall_IsReportedToTheOutageTracker()
    {
        var harness = new Harness(_ => ScriptedGraphQlHandler.GraphQlError(
            "AniList is temporarily disabled.", HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        harness.Outage.Received(1).ReportFailure(Arg.Any<AniListApiException>());
        harness.Outage.DidNotReceive().ReportSuccess();
    }

    [Fact]
    public async Task ABlipThatSucceedsOnRetry_IsNotReportedAsAnOutage()
    {
        // Only a failed retry counts. Reporting the first attempt would flash the outage banner for
        // a hiccup the user never noticed.
        var harness = new Harness(Alternating(
            () => ScriptedGraphQlHandler.Raw(HttpStatusCode.BadRequest, """{"errors":[{"message":"Invalid token"}]}"""),
            () => ScriptedGraphQlHandler.Data(SomeMedia)));

        await harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken);

        harness.Outage.Received(1).ReportSuccess();
        harness.Outage.DidNotReceive().ReportFailure(Arg.Any<Exception>());
    }

    // ── The single retry ─────────────────────────────────────────────

    [Fact]
    public async Task ATransientFailure_IsRetriedOnceAndCanSucceed()
    {
        // AniList has been observed rejecting a valid token with 400 "Invalid token" and accepting
        // the identical token seconds later.
        var harness = new Harness(Alternating(
            () => ScriptedGraphQlHandler.Raw(HttpStatusCode.BadRequest, """{"errors":[{"message":"Invalid token"}]}"""),
            () => ScriptedGraphQlHandler.Data(SomeMedia)));

        var (media, _) = await harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken);

        Assert.NotNull(media);
        Assert.Equal(2, harness.Handler.CallCount);
    }

    [Fact]
    public async Task APersistentTransientFailure_GivesUpAfterExactlyOneRetry()
    {
        // The retry is a blip absorber, not a loop — a genuinely down API must surface quickly.
        var harness = new Harness(_ => ScriptedGraphQlHandler.Raw(
            HttpStatusCode.BadRequest, """{"errors":[{"message":"Invalid token"}]}"""));

        await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal(2, harness.Handler.CallCount);
    }

    [Fact]
    public async Task ANonTransientFailure_IsNotRetriedAtAll()
    {
        // A rate-limit response is the one thing a retry makes strictly worse.
        var harness = new Harness(_ => ScriptedGraphQlHandler.GraphQlError(
            "Too Many Requests.", HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<AniListApiException>(
            () => harness.Client.GetMediaAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal(1, harness.Handler.CallCount);
    }

    /// <summary>
    /// Answers the first call from <paramref name="first"/> and every later one from
    /// <paramref name="rest"/>. Both are factories because the client disposes each response it
    /// receives, so a shared instance would fail the moment a test made a second call.
    /// </summary>
    private static Func<CapturedGraphQlRequest, HttpResponseMessage> Alternating(
        Func<HttpResponseMessage> first, Func<HttpResponseMessage> rest)
    {
        var served = 0;
        return _ => Interlocked.Increment(ref served) == 1 ? first() : rest();
    }

    private sealed class Harness
    {
        private string? _token = "token-abc";

        public Harness(string dataJson)
            : this(_ => ScriptedGraphQlHandler.Data(dataJson))
        {
        }

        public Harness(Func<CapturedGraphQlRequest, HttpResponseMessage> responder)
        {
            Handler = new ScriptedGraphQlHandler(responder);

            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(_ => _token);

            Client = new AniListClient(
                new HttpClient(Handler),
                auth,
                Outage,
                NullLogger<AniListClient>.Instance);
        }

        public ScriptedGraphQlHandler Handler { get; }

        public IOutageStateService Outage { get; } = Substitute.For<IOutageStateService>();

        public AniListClient Client { get; }

        public void SignOut() => _token = null;
    }
}
