using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services
{
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

            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                stopwatch.Stop();
                _logger.LogInformation("HTTP {StatusCode} {Method} {Uri} in {Elapsed}ms",
                    (int)response.StatusCode,
                    request.Method,
                    request.RequestUri,
                    stopwatch.ElapsedMilliseconds);
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "HTTP failed {Method} {Uri} after {Elapsed}ms",
                    request.Method,
                    request.RequestUri,
                    stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
