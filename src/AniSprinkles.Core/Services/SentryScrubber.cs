using AniSprinkles.Utilities;

namespace AniSprinkles.Services;

/// <summary>
/// Last-chance redaction for anything on its way to Sentry (#124).
/// <para>
/// <see cref="ErrorReportService"/> is the only <c>CaptureException</c> call site in the app, and
/// <see cref="AniListClient"/> now redacts server-derived text before it ever reaches an exception
/// message — but Sentry also captures unhandled exceptions on its own, by a path that goes through
/// neither. This runs on every outbound event so a future throw site cannot quietly reopen the hole.
/// </para>
/// <para>
/// Only the human-readable strings are touched. The stack frames, exception types and fingerprint
/// inputs are left exactly as they are, so grouping is unaffected.
/// </para>
/// </summary>
public static class SentryScrubber
{
    /// <summary>
    /// Redacts an event in place and returns it, shaped for <c>options.SetBeforeSend</c>.
    /// </summary>
    public static SentryEvent Scrub(SentryEvent evt)
    {
        if (evt.SentryExceptions is not null)
        {
            // Materialize before mutating, and assign the materialized list back. SentryExceptions
            // is typed as IEnumerable, so a lazily-projected sequence would hand each enumeration a
            // fresh set of objects — mutating in a plain foreach would then redact copies Sentry
            // never sends, and a test built on a List would pass anyway.
            var exceptions = evt.SentryExceptions.ToList();
            foreach (var exception in exceptions)
            {
                exception.Value = SensitiveText.Redact(exception.Value);
            }

            evt.SentryExceptions = exceptions;
        }

        if (evt.Message is not null)
        {
            evt.Message = new SentryMessage
            {
                Message = SensitiveText.Redact(evt.Message.Message),
                Formatted = SensitiveText.Redact(evt.Message.Formatted),
                Params = evt.Message.Params,
            };
        }

        // Breadcrumbs are deliberately not walked. Every one this app adds is either a fixed string
        // or a method + path pair (LoggingHandler trims the URI with GetLeftPart(UriPartial.Path)),
        // and SentryEvent exposes them as a read-only collection, so rewriting them would mean
        // rebuilding the event. Revisit if a breadcrumb ever carries a server-supplied message.
        return evt;
    }
}
