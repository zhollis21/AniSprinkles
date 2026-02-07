using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sentry;

namespace AniSprinkles.Services
{
    public class ErrorReportService
    {
        private static readonly Regex BearerTokenRegex =
            new(@"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ILogger<ErrorReportService> _logger;

        public ErrorReportService(ILogger<ErrorReportService> logger)
        {
            _logger = logger;
        }

        public string Record(Exception ex, string context)
        {
            var summary = $"{context}: {ex.Message}";
            var details = $"{context}{Environment.NewLine}{ex}";

            summary = Redact(summary);
            details = Redact(details);

            _logger.LogError(ex, "{Context}", context);
            SentrySdk.CaptureException(ex);

            return details;
        }

        private static string Redact(string value)
            => BearerTokenRegex.Replace(value, "Bearer <redacted>");
    }
}
