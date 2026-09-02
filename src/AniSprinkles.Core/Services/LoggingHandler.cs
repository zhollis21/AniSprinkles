using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

public class LoggingHandler : DelegatingHandler
{
    /// <summary>
    /// The caller's own <see cref="CancellationToken"/>, recorded on the request so this handler can
    /// tell "the app abandoned this" from "this failed".
    /// <para>
    /// Neither of the two things a handler can normally read is sufficient. The token passed down
    /// the pipeline is HttpClient's *linked* token, cancelled both by the caller and by
    /// <see cref="HttpClient.Timeout"/> — and the exception is no better, because on Android both
    /// paths tear the socket down and surface as <c>WebException("Socket closed")</c> rather than
    /// as an <see cref="OperationCanceledException"/>. Only the caller knows which happened, so it
    /// says so here. <c>AniListRateLimitHandler</c>'s retry template copies Options across for the
    /// same reason.
    /// </para>
    /// </summary>
    public static readonly HttpRequestOptionsKey<CancellationToken> CallerCancellationToken =
        new("AniSprinkles.CallerCancellationToken");

    private readonly ILogger<LoggingHandler> _logger;

    public LoggingHandler(ILogger<LoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("HTTP {Method} {Uri}", request.Method, request.RequestUri);
        var breadcrumbsUri = request.RequestUri?.GetLeftPart(UriPartial.Path) ?? request.RequestUri?.ToString() ?? "unknown";
        SentrySdk.AddBreadcrumb(
            message: $"HTTP {request.Method} {breadcrumbsUri}",
            category: "http",
            type: "http",
            data: new Dictionary<string, string>
            {
                ["method"] = request.Method.Method,
                ["uri"] = breadcrumbsUri
            });

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogInformation("HTTP {StatusCode} {Method} {Uri} in {Elapsed}ms",
                (int)response.StatusCode,
                request.Method,
                request.RequestUri,
                stopwatch.ElapsedMilliseconds);
            SentrySdk.AddBreadcrumb(
                message: $"HTTP {(int)response.StatusCode} {request.Method} {breadcrumbsUri}",
                category: "http",
                type: "http",
                data: new Dictionary<string, string>
                {
                    ["status"] = ((int)response.StatusCode).ToString(),
                    ["elapsed_ms"] = stopwatch.ElapsedMilliseconds.ToString()
                });
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // A request the app itself abandoned is not an error worth a Sentry event. Without this,
            // every debounced search the user typed straight through and every page left mid-load
            // reported an HTTP failure.
            //
            // Read the CALLER's token, not the one passed down the pipeline: that one is HttpClient's
            // linked token, so HttpClient.Timeout cancels it too, and a 100-second timeout would be
            // filed away as a cancellation — hiding exactly the failure this handler exists to
            // report. When no caller recorded a token we log Error, because missing a cancellation
            // costs one redundant Sentry event while missing a real failure costs the report that
            // would have explained an outage.
            var cancelled = request.Options.TryGetValue(CallerCancellationToken, out var callerToken)
                && callerToken.IsCancellationRequested;
            if (cancelled)
            {
                _logger.LogInformation("HTTP cancelled {Method} {Uri} after {Elapsed}ms",
                    request.Method,
                    request.RequestUri,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogError(ex, "HTTP failed {Method} {Uri} after {Elapsed}ms",
                    request.Method,
                    request.RequestUri,
                    stopwatch.ElapsedMilliseconds);
            }

            SentrySdk.AddBreadcrumb(
                message: $"HTTP {(cancelled ? "cancelled" : "failed")} {request.Method} {breadcrumbsUri}",
                category: "http",
                type: "http",
                level: cancelled ? BreadcrumbLevel.Info : BreadcrumbLevel.Error,
                data: new Dictionary<string, string>
                {
                    ["elapsed_ms"] = stopwatch.ElapsedMilliseconds.ToString()
                });
            throw;
        }
    }
}
