using System.Net;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// A <see cref="DelegatingHandler"/> that makes the shared AniList <see cref="HttpClient"/>
/// rate-limit aware. AniList's GraphQL endpoint runs at ~90 req/min nominal (currently degraded
/// to ~30/min) with a burst limiter, and replies to overflow with HTTP 429 + a <c>Retry-After</c>
/// header. This handler:
/// <list type="bullet">
/// <item>Serializes all outgoing requests through a single <see cref="SemaphoreSlim"/> so we never
/// trip the burst limiter (sequential requests are naturally spaced by round-trip latency).</item>
/// <item>Reads <c>X-RateLimit-Remaining</c> / <c>X-RateLimit-Reset</c> and, when the budget is
/// nearly exhausted, delays the next request until the window resets (adaptive spacing).</item>
/// <item>On 429, waits <c>Retry-After</c> and retries transparently — but only up to
/// <see cref="_maxRetries"/> times and only if the wait is under <see cref="_maxAutoRetryWait"/>,
/// so the UI never hangs. Otherwise it surfaces <see cref="ApiErrorKind.RateLimited"/>.</item>
/// </list>
/// All time-based waits go through an injected <see cref="TimeProvider"/> so tests can drive
/// retry/backoff deterministically with a fake clock.
/// </summary>
public sealed class AniListRateLimitHandler : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time;
    private readonly ILogger<AniListRateLimitHandler> _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _maxAutoRetryWait;
    private readonly TimeSpan _minSpacing;

    // Guarded by _gate (requests are serialized, so a plain field is safe).
    private DateTimeOffset _nextAllowedSend = DateTimeOffset.MinValue;

    public AniListRateLimitHandler(
        TimeProvider timeProvider,
        ILogger<AniListRateLimitHandler> logger,
        int maxRetries = 3,
        TimeSpan? maxAutoRetryWait = null,
        TimeSpan? minSpacing = null)
    {
        _time = timeProvider;
        _logger = logger;
        _maxRetries = maxRetries;
        _maxAutoRetryWait = maxAutoRetryWait ?? TimeSpan.FromSeconds(5);
        _minSpacing = minSpacing ?? TimeSpan.Zero;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the request so it can be re-issued on retry (an HttpRequestMessage / its content
        // stream can only be sent once).
        var template = await RequestTemplate.CaptureAsync(request, cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await WaitForSlotAsync(cancellationToken).ConfigureAwait(false);

                var response = await base.SendAsync(template.Build(), cancellationToken).ConfigureAwait(false);
                UpdateSpacingFromHeaders(response);

                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    return response;
                }

                var retryAfter = GetRetryAfter(response, attempt);
                response.Dispose();

                if (attempt >= _maxRetries || retryAfter > _maxAutoRetryWait)
                {
                    _logger.LogWarning(
                        "AniList 429 not auto-retried (attempt {Attempt}, retryAfter {RetryAfter}s)",
                        attempt, retryAfter.TotalSeconds);
                    throw new AniListApiException(
                        ApiErrorKind.RateLimited,
                        "AniList rate limit reached. Please wait a moment and try again.");
                }

                _logger.LogWarning(
                    "AniList 429 — retrying after {RetryAfter}s (attempt {Attempt})",
                    retryAfter.TotalSeconds, attempt + 1);
                await Task.Delay(retryAfter, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        var wait = _nextAllowedSend - _time.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private void UpdateSpacingFromHeaders(HttpResponseMessage response)
    {
        var next = _time.GetUtcNow() + _minSpacing;

        // When we're about to run dry, hold off until the rate window resets.
        if (TryGetHeaderLong(response, "X-RateLimit-Remaining", out var remaining) && remaining <= 1
            && TryGetHeaderLong(response, "X-RateLimit-Reset", out var resetUnix))
        {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
            if (resetAt > next)
            {
                next = resetAt;
            }
        }

        _nextAllowedSend = next;
    }

    private TimeSpan GetRetryAfter(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (retryAfter.Date is { } date)
            {
                var untilDate = date - _time.GetUtcNow();
                if (untilDate > TimeSpan.Zero)
                {
                    return untilDate;
                }
            }
        }

        // No usable header — exponential backoff (1s, 2s, 4s, ...).
        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private static bool TryGetHeaderLong(HttpResponseMessage response, string name, out long value)
    {
        value = 0;
        if (response.Headers.TryGetValues(name, out var values))
        {
            foreach (var raw in values)
            {
                if (long.TryParse(raw, out value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// A buffered snapshot of an <see cref="HttpRequestMessage"/> that can rebuild fresh,
    /// independently-sendable copies for each retry attempt.
    /// </summary>
    private sealed class RequestTemplate
    {
        private HttpMethod _method = HttpMethod.Get;
        private Uri? _requestUri;
        private Version _version = HttpVersion.Version11;
        private byte[]? _content;
        private List<KeyValuePair<string, IEnumerable<string>>> _requestHeaders = [];
        private List<KeyValuePair<string, IEnumerable<string>>> _contentHeaders = [];
        private List<KeyValuePair<string, object?>> _options = [];

        public static async Task<RequestTemplate> CaptureAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var template = new RequestTemplate
            {
                _method = request.Method,
                _requestUri = request.RequestUri,
                _version = request.Version,
                _requestHeaders = request.Headers.ToList(),
                // Every send below goes out as a rebuilt message, including the first one, so an
                // Option the caller recorded reaches the inner handlers only if it is copied here.
                // LoggingHandler.CallerCancellationToken is the first thing that depends on it.
                _options = ((IDictionary<string, object?>)request.Options).ToList(),
            };

            if (request.Content is not null)
            {
                template._content = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                template._contentHeaders = request.Content.Headers.ToList();
            }

            return template;
        }

        public HttpRequestMessage Build()
        {
            var request = new HttpRequestMessage(_method, _requestUri) { Version = _version };

            foreach (var header in _requestHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var option in _options)
            {
                ((IDictionary<string, object?>)request.Options)[option.Key] = option.Value;
            }

            if (_content is not null)
            {
                request.Content = new ByteArrayContent(_content);
                request.Content.Headers.Clear();
                foreach (var header in _contentHeaders)
                {
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return request;
        }
    }
}
