using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

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

        public string? LastErrorSummary { get; private set; }
        public string? LastErrorDetails { get; private set; }
        public DateTimeOffset? LastErrorAt { get; private set; }

        public string Record(Exception ex, string context)
        {
            var summary = $"{context}: {ex.Message}";
            var details = $"{context}{Environment.NewLine}{ex}";

            summary = Redact(summary);
            details = Redact(details);

            LastErrorSummary = summary;
            LastErrorDetails = details;
            LastErrorAt = DateTimeOffset.Now;

            _logger.LogError(ex, "{Context}", context);

            return details;
        }

        public void Clear()
        {
            LastErrorSummary = null;
            LastErrorDetails = null;
            LastErrorAt = null;
        }

        private static string Redact(string value)
            => BearerTokenRegex.Replace(value, "Bearer <redacted>");
    }
}
