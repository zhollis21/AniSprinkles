#if DEBUG
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services.FaultInjection;

/// <summary>
/// Injects synthetic HTTP failures into the AniList pipeline so the code that actually handles
/// real-world failure gets to run (#125, seam 2).
/// <para>
/// Installed <em>innermost</em> — <c>rateLimit → logging → fault → HttpClientHandler</c>. That
/// position is load-bearing: everything outside it treats the synthetic answer as if it came off the
/// wire, so <c>AniListRateLimitHandler</c>, <c>LoggingHandler</c>,
/// <c>AniListClient.SendAsync</c> retry-once and <c>AniListErrorClassifier</c> all execute for real.
/// <see cref="FaultInjectingAniListClient"/> sits above all of them and reaches none of it; that is
/// why these are two seams rather than one.
/// </para>
/// <para>
/// The trade in the other direction: this needs the real client and a signed-in session, so it does
/// <em>not</em> compose with the CI fixtures. Arm the client seam when you want a loaded screen and
/// a broken next call; arm this one when the pipeline itself is what you are testing.
/// </para>
/// <para>
/// Rehearsing the 429 path here is also the only way to exercise <c>AniListRateLimitHandler</c>
/// without actually getting rate-limited by AniList, which matters under a budget degraded to
/// ~30 req/min.
/// </para>
/// </summary>
public sealed class FaultInjectingHttpHandler(
    FaultState state,
    ILogger<FaultInjectingHttpHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Cheap disarmed path: one lock acquisition, no body read. Reading the GraphQL operation
        // name means buffering the request content, which is not worth doing on every request in
        // every Debug build just to discover there is nothing armed.
        //
        // Current-then-Decide is two reads rather than one atomic step, so a profile cleared between
        // them lets one request through unfaulted. That is fine for debug tooling and not worth a
        // wider lock — the alternative is holding the gate across a content read.
        if (state.Current is not { Layer: FaultLayer.Http } armed)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var operation = armed.OperationPrefix is null
            ? string.Empty
            : await ReadOperationNameAsync(request, cancellationToken).ConfigureAwait(false);

        var decision = state.Decide(operation, FaultLayer.Http);
        if (decision.IsPassThrough)
        {
            // Say what the operation actually was when a targeted profile is armed and misses.
            //
            // This seam keys off the GraphQL operationName, while the client seam keys off the
            // IAniListClient method name — and the two genuinely differ, beyond any mechanical
            // stripping of Get/Load/Async: GetMyAnimeListAsync is "MediaListCollection" here, and
            // SearchMediaPageAsync is "Search". So `fault GetStudio ... -layer http` matches nothing
            // and, without this line, is indistinguishable from a seam that does not work.
            //
            // Only reachable with a targeted Http profile armed, which is deliberate and rare, so it
            // cannot flood the log during ordinary use.
            if (armed.OperationPrefix is { } prefix)
            {
                logger.LogInformation(
                    "FAULT http no match: armed op={Prefix}, this request was {Operation}. "
                    + "The http layer matches the GraphQL operationName, not the IAniListClient method name.",
                    prefix,
                    operation.Length == 0 ? "<unreadable>" : operation);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (decision.Kind is not { } kind)
        {
            // Delay-only profile: slow the real request down without breaking it.
            await Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // RateLimited spends Delay as Retry-After instead of as latency — see FaultProfile.Delay.
        if (decision.Delay > TimeSpan.Zero && kind != ApiErrorKind.RateLimited)
        {
            await Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
        }

        if (kind == ApiErrorKind.Network)
        {
            // Thrown rather than returned: AniListClient.SendAsyncCore catches HttpRequestException
            // (and a timeout-wrapped TaskCanceledException) and is what maps them to Network.
            logger.LogWarning("FAULT http throwing network failure for {Operation}", Describe(operation));
            throw new HttpRequestException("Simulated network failure (fault injection).");
        }

        var response = decision.AsGraphQlError
            ? BuildGraphQlErrorResponse(kind)
            : BuildHttpErrorResponse(kind, decision.Delay);

        logger.LogWarning(
            "FAULT http answering {Operation} with {Status}{Shape}",
            Describe(operation),
            (int)response.StatusCode,
            decision.AsGraphQlError ? " (GraphQL errors body)" : string.Empty);

        return response;
    }

    private static string Describe(string operation) => operation.Length == 0 ? "<any>" : operation;

    /// <summary>
    /// HTTP 200 carrying a GraphQL <c>errors</c> array — the shape that routes through
    /// <c>ClassifyGraphQlError</c> rather than <c>ClassifyHttpError</c>.
    /// </summary>
    private static HttpResponseMessage BuildGraphQlErrorResponse(ApiErrorKind kind)
        => new(HttpStatusCode.OK) { Content = GraphQlErrorBody(MessageFor(kind)) };

    private static HttpResponseMessage BuildHttpErrorResponse(ApiErrorKind kind, TimeSpan retryAfter)
    {
        var message = MessageFor(kind);

        // Status codes chosen to match what AniList actually returns, because the classifier keys off
        // the real shapes: an outage is a 403 with a "disabled" message as often as a 5xx, and a
        // rejected OAuth token comes back 400 with "Invalid token" rather than 401 (see the comment
        // on AniListErrorClassifier.ClassifyHttpError).
        var status = kind switch
        {
            ApiErrorKind.RateLimited => HttpStatusCode.TooManyRequests,
            ApiErrorKind.ServiceOutage => HttpStatusCode.ServiceUnavailable,
            ApiErrorKind.Authentication => HttpStatusCode.BadRequest,
            ApiErrorKind.NotFound => HttpStatusCode.NotFound,
            _ => HttpStatusCode.InternalServerError,
        };

        var response = new HttpResponseMessage(status) { Content = GraphQlErrorBody(message) };

        if (kind == ApiErrorKind.RateLimited)
        {
            // Under AniListRateLimitHandler's maxAutoRetryWait (5 s by default) this is consumed
            // silently and the request is retried; over it, the handler gives up and surfaces
            // RateLimited. Arming the delay is how you choose which branch you are looking at.
            var delta = retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        }

        return response;
    }

    private static StringContent GraphQlErrorBody(string message)
    {
        var payload = JsonSerializer.Serialize(new
        {
            errors = new[] { new { message } },
        });

        return new StringContent(payload, Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// Messages carry the markers <c>AniListErrorClassifier</c> matches on, so a synthetic failure
    /// classifies down the same branch a real one would.
    /// </summary>
    private static string MessageFor(ApiErrorKind kind) => kind switch
    {
        ApiErrorKind.ServiceOutage => "AniList API has been temporarily disabled due to stability issues.",
        ApiErrorKind.Authentication => "Invalid token",
        ApiErrorKind.NotFound => "Not Found.",
        ApiErrorKind.RateLimited => "Too Many Requests",
        _ => "Something unexpected happened.",
    };

    /// <summary>
    /// The GraphQL <c>operationName</c> from the request body, so an Http-layer profile can target a
    /// single query. Returns empty on anything unparseable — a profile that cannot read the operation
    /// should decline to match rather than fault every request in sight.
    /// </summary>
    private static async Task<string> ReadOperationNameAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return string.Empty;
        }

        try
        {
            // Safe to read: by the time this runs the content is a buffered StringContent (from
            // AniListClient) or ByteArrayContent (rebuilt by AniListRateLimitHandler for retries),
            // neither of which is a one-shot stream.
            var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("operationName", out var name)
                ? name.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
#endif
