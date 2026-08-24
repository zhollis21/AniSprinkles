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
        SentrySdk.CaptureException(ex);

        return details;
    }
}
