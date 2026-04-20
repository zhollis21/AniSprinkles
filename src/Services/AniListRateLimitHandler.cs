using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// Observes AniList rate-limit headers on every response and emits structured
/// warnings when limits are approached or exceeded. Does not retry; the
/// response (including 429 Too Many Requests) is returned unchanged so
/// callers and the OTel / Sentry pipelines surface the failure consistently.
/// </summary>
/// <remarks>
/// AniList publishes <c>X-RateLimit-Limit</c>, <c>X-RateLimit-Remaining</c>,
/// and (on 429) <c>Retry-After</c>. Automatic retry is intentionally out of
/// scope: 429 on a rate-limited session is typically sustained, so an
/// in-process retry can extend the blocked window rather than recover from it.
/// The standard resilience handler registered via Aspire service defaults
/// handles transient 5xx / timeout cases; this handler provides the
/// observability foundation for future rate-limit work (see issue #36).
/// </remarks>
public sealed class AniListRateLimitHandler : DelegatingHandler
{
    // Warn when AniList reports fewer than this many requests remain in the
    // current window. AniList's default window allows 90 req/min, so 10 is a
    // conservative "slow down" threshold that still fires often enough to be
    // useful without flooding logs on a quiet session.
    private const int LowRemainingThreshold = 10;

    // Static Meter — one per library is the .NET guidance. OTel picks it up by
    // name via metrics.AddMeter("AniSprinkles.AniList") in MauiProgram.
    private static readonly Meter s_meter = new("AniSprinkles.AniList");

    private static readonly Counter<long> s_requestsCounter =
        s_meter.CreateCounter<long>("anilist.requests", unit: "{request}", description: "AniList HTTP responses observed, tagged by status code.");

    private static readonly Counter<long> s_throttledCounter =
        s_meter.CreateCounter<long>("anilist.ratelimit.throttled", unit: "{response}", description: "AniList 429 Too Many Requests responses observed.");

    private static readonly Histogram<int> s_remainingHistogram =
        s_meter.CreateHistogram<int>("anilist.ratelimit.remaining", unit: "{request}", description: "Value of the X-RateLimit-Remaining header on AniList responses.");

    private readonly ILogger<AniListRateLimitHandler> _logger;

    public AniListRateLimitHandler(ILogger<AniListRateLimitHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var statusCode = (int)response.StatusCode;
        s_requestsCounter.Add(1, new KeyValuePair<string, object?>("anilist.status_code", statusCode));

        LogRemaining(response, statusCode);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            s_throttledCounter.Add(1);
            LogRateLimited(response);
        }

        return response;
    }

    private void LogRemaining(HttpResponseMessage response, int statusCode)
    {
        if (!TryReadIntHeader(response, "X-RateLimit-Remaining", out var remaining))
        {
            return;
        }

        s_remainingHistogram.Record(remaining, new KeyValuePair<string, object?>("anilist.status_code", statusCode));

        if (remaining >= LowRemainingThreshold)
        {
            return;
        }

        _ = TryReadIntHeader(response, "X-RateLimit-Limit", out var limit);

        _logger.LogWarning(
            "AniList rate-limit remaining low: {Remaining}/{Limit} (threshold {Threshold})",
            remaining,
            limit,
            LowRemainingThreshold);
    }

    private void LogRateLimited(HttpResponseMessage response)
    {
        var retryAfterSeconds = ReadRetryAfterSeconds(response);
        _ = TryReadIntHeader(response, "X-RateLimit-Remaining", out var remaining);

        _logger.LogWarning(
            "AniList returned 429 Too Many Requests; RetryAfterSeconds={RetryAfterSeconds} Remaining={Remaining}",
            retryAfterSeconds,
            remaining);
    }

    private static bool TryReadIntHeader(HttpResponseMessage response, string name, out int value)
    {
        value = 0;
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return false;
        }

        var raw = values.FirstOrDefault();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static int ReadRetryAfterSeconds(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry is null)
        {
            return 0;
        }

        if (retry.Delta is { } delta)
        {
            return (int)Math.Max(0, delta.TotalSeconds);
        }

        // HTTP-date form: fall back to zero. AniList publishes delta-seconds;
        // the date form is tracked only for completeness.
        return 0;
    }
}
