using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

public class LoggingHandler : DelegatingHandler
{
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

            // A request the app itself abandoned is not an error worth a Sentry event. It rarely
            // arrives as an OperationCanceledException: on Android, cancelling closes the socket
            // under the in-flight read and the failure surfaces as WebException("Socket closed"),
            // so the token — not the exception type — is what tells the two apart. Without this,
            // every debounced search the user typed straight through and every page left mid-load
            // reported an HTTP failure.
            var cancelled = cancellationToken.IsCancellationRequested;
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
