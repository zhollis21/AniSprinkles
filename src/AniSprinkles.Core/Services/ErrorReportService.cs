using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

public class ErrorReportService
{
    private readonly ILogger<ErrorReportService> _logger;

    public ErrorReportService(ILogger<ErrorReportService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs and reports a handled exception, and returns the detail text for the on-screen error
    /// panel.
    /// </summary>
    /// <remarks>
    /// The raw <paramref name="ex"/> is handed to both sinks on purpose. Redaction happens at the
    /// source now (<see cref="SensitiveText"/>, applied in <see cref="AniListClient"/>), so the
    /// exception message never carries a token to begin with — and passing a rebuilt exception to
    /// Sentry would cost the stack trace its grouping depends on. The <see cref="SensitiveText"/>
    /// call below is belt-and-braces for the returned string, not the only line of defence it used
    /// to be; <c>SentryScrubber</c> is the matching backstop on the Sentry side.
    /// </remarks>
    public string Record(Exception ex, string context)
    {
        var details = SensitiveText.Redact($"{context}{Environment.NewLine}{ex}");

        _logger.LogError(ex, "{Context}", context);

        // This call does not create the Sentry event, and its return value is always SentryId.Empty.
        // Traced while building #112, and confirmed by the `mechanism: SentryLogger` tag on the
        // events themselves:
        //
        //   1. SentryLoggingOptions.MinimumEventLevel defaults to Error, so the LogError above is
        //      what the ILogger integration captures. That call gets the real id.
        //   2. This one then hits DuplicateEventDetectionEventProcessor — SentryOptions.DeduplicateMode
        //      defaults to `All ^ InnerException`, which still includes SameExceptionInstance — so the
        //      processor drops it and SentryClient returns SentryId.Empty.
        //   3. Hub.CaptureEvent assigns `scope.LastEventId = id` unguarded, so step 2 also overwrites
        //      the good id from step 1. SentrySdk.LastEventId is therefore empty too.
        //
        // It stays because it is the guarantee: if the logging level or a filter ever stops this
        // class's LogError reaching Sentry, there is no duplicate to drop and this becomes the only
        // path. Just never read its result, and never expect SentrySdk.LastEventId to name it.
        SentrySdk.CaptureException(ex);

        return details;
    }
}
