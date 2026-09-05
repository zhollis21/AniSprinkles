using System.Text;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// Sends a diagnostic report to Sentry as an event with the log attached (#112).
/// <para>
/// Sentry is the only delivery route. It reaches the maintainer without asking anything of the user,
/// which is what a real bug report needs — and its silence is covered at the UI end instead, by a
/// popup that states what is about to be sent and a confirmation that names it back.
/// </para>
/// </summary>
public sealed class SentryDiagnosticsSink : IDiagnosticsSink
{
    /// <summary>
    /// How long to wait for the envelope to leave. Reports are sent from a screen the user is about
    /// to leave — often the very screen that is broken — so returning before the transport has run
    /// risks the process going away with the report still queued. Bounded so a dead network cannot
    /// hang the confirmation indefinitely.
    /// </summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<SentryDiagnosticsSink> _logger;

    public SentryDiagnosticsSink(ILogger<SentryDiagnosticsSink> logger)
    {
        _logger = logger;
    }

    public async Task<bool> SendAsync(string report, string? description, CancellationToken cancellationToken = default)
    {
        if (!SentrySdk.IsEnabled)
        {
            // No DSN, or the SDK never initialised. Worth saying out loud: the alternative is telling
            // the user their report was sent when nothing left the device.
            _logger.LogWarning("Diagnostic report not sent: the Sentry SDK is not enabled.");
            return false;
        }

        try
        {
            var attachment = Encoding.UTF8.GetBytes(report);

            var eventId = SentrySdk.CaptureMessage(
                "Diagnostic report (user-initiated)",
                scope => scope.AddAttachment(
                    attachment,
                    DiagnosticsReportBuilder.AttachmentFileName,
                    AttachmentType.Default,
                    "text/plain"),
                SentryLevel.Info);

            if (eventId == SentryId.Empty)
            {
                _logger.LogWarning("Diagnostic report not sent: Sentry returned an empty event id.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                // Sent as feedback linked to the event rather than folded into the message, so it
                // shows up in Sentry as a user report. Redacted again here rather than trusting the
                // report pass: this is free text the user could have pasted anything into, and it
                // takes a different route to Sentry than the attachment does.
                SentrySdk.CaptureFeedback(new SentryFeedback(
                    SensitiveText.Redact(description.Trim()),
                    contactEmail: null,
                    name: null,
                    replayId: null,
                    url: null,
                    associatedEventId: eventId));
            }

            await SentrySdk.FlushAsync(FlushTimeout).WaitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Diagnostic report sent ({EventId}, {Bytes} bytes).", eventId, attachment.Length);
            return true;
        }
        catch (OperationCanceledException)
        {
            // The flush was abandoned, not the capture — the envelope may still go out on its own.
            // Reported as a failure anyway: a confirmation the user cannot rely on is worse than none.
            _logger.LogWarning("Diagnostic report send was cancelled while flushing.");
            return false;
        }
        catch (Exception ex)
        {
            // Deliberately broad. This runs off a button the user pressed because something was
            // already wrong; the reporting path throwing on top of that is the worst outcome available.
            _logger.LogError(ex, "Diagnostic report failed to send.");
            return false;
        }
    }
}
