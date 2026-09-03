using System.Text;
using AniSprinkles.Utilities;

namespace AniSprinkles.Services;

/// <summary>
/// The environment facts that head a diagnostic report. Passed in rather than read here so the
/// builder stays pure: every one of these comes from MAUI Essentials (<c>AppInfo</c>,
/// <c>DeviceInfo</c>), which throws off-device and would make the builder untestable.
/// </summary>
public readonly record struct DiagnosticsContext(
    string AppVersion,
    string BuildConfiguration,
    string Device,
    string OsVersion,
    bool IsSignedIn);

/// <summary>
/// Assembles the text a user sends when they report a problem (#112): a short header, whatever they
/// typed, the tail of the previous session if one was flushed, and the current in-memory ring.
/// <para>
/// Pure and synchronous on purpose. The redaction pass below is the only thing standing between the
/// log and Sentry — <c>SentryScrubber</c> runs on <c>SetBeforeSend</c> over the
/// <c>SentryEvent</c> and does <b>not</b> walk attachments, so a token reaching the report here
/// would reach Sentry intact. Keeping the assembly pure is what lets that be tested directly.
/// </para>
/// </summary>
public static class DiagnosticsReportBuilder
{
    /// <summary>The name the report is attached under in Sentry.</summary>
    public const string AttachmentFileName = "anisprinkles-diagnostics.log";

    /// <summary>Header for the section holding what the user typed, omitted when they typed nothing.</summary>
    public const string DescriptionHeader = "── what the user was doing ──";

    /// <summary>Header for the current in-memory ring.</summary>
    public const string CurrentSessionHeader = "── this session ──";

    /// <summary>Stands in for the ring when there is nothing in it, so an empty report cannot be
    /// mistaken for a truncated one.</summary>
    public const string NoActivityPlaceholder = "(no activity recorded in the window)";

    public static string Build(
        DiagnosticsContext context,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> previousSession,
        IReadOnlyList<string> currentSession,
        string? description)
    {
        var builder = new StringBuilder();

        builder.AppendLine("AniSprinkles diagnostic report");
        builder.AppendLine($"captured: {capturedAt:O}");
        builder.AppendLine($"version:  {context.AppVersion} ({context.BuildConfiguration})");
        builder.AppendLine($"device:   {context.Device} — {context.OsVersion}");
        // Whether they were signed in changes which code paths could even have run, and it is the one
        // account fact worth stating outright rather than leaving to be inferred from AUTH traces.
        builder.AppendLine($"signed in: {(context.IsSignedIn ? "yes" : "no")}");

        // No cross-reference to the automatic exception event, deliberately. An earlier draft printed
        // one, and on device it was always absent: the exception does reach Sentry, but neither
        // ErrorReportService's CaptureException return value nor SentrySdk.LastEventId holds its id by
        // the time a report is built. Correlating on the exception text and the timestamps below —
        // both of which this report already carries in full — works, and a header field that is
        // reliably empty is worse than no field at all.

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine();
            builder.AppendLine(DescriptionHeader);
            builder.AppendLine(description.Trim());
        }

        if (previousSession.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(DiagnosticsSessionLog.PreviousSessionHeader);
            foreach (var line in previousSession)
            {
                builder.AppendLine(line);
            }
        }

        builder.AppendLine();
        builder.AppendLine(CurrentSessionHeader);
        if (currentSession.Count == 0)
        {
            builder.AppendLine(NoActivityPlaceholder);
        }
        else
        {
            foreach (var line in currentSession)
            {
                builder.AppendLine(line);
            }
        }

        // One pass over the finished text rather than per-line, which is what makes line structure
        // irrelevant to redaction. That matters because records are not strictly one line each —
        // an appended stack trace keeps its breaks — but the token pattern is `Bearer\s+…`, and `\s`
        // spans newlines, so a credential wrapped across a break is still matched. Scanning the whole
        // string also means a section someone adds later cannot quietly skip the pass.
        return SensitiveText.Redact(builder.ToString());
    }
}
