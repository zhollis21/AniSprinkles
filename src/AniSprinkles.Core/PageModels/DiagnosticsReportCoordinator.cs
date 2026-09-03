using AniSprinkles.Services;
using AniSprinkles.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

/// <summary>
/// The whole send-a-diagnostic-report flow, in one place so both entry points behave identically:
/// the Settings card and the Report action on any error card (#112).
/// <para>
/// Order matters and is the point of the type. Disclose first — the user sees what is about to leave
/// their device before anything is collected, not after. Then snapshot, build, redact, send, and say
/// what happened. Sentry is silent to the sender, so the popup and the confirmation are the only
/// things telling the user their report exists.
/// </para>
/// </summary>
public sealed class DiagnosticsReportCoordinator
{
    /// <summary>
    /// What the popup promises. Kept here rather than in the popup's XAML so the promise and the
    /// thing that fulfils it live in one file and are asserted together — a disclosure that drifts
    /// from what is actually collected is worse than no disclosure, because it is trusted.
    /// </summary>
    public const string DisclosureSummary =
        "This sends the last 5 minutes of app activity: the screens you opened, the AniList ids they "
        + "were for, the requests the app made and how they failed, plus your app version and device "
        + "model. Your AniList login is never included.";

    /// <summary>Shown after a successful send. Names the thing the popup promised, so the two read
    /// back against each other rather than the user having to take "sent" on faith.</summary>
    public const string SentMessage = "Diagnostics sent — last 5 minutes of activity.";

    /// <summary>Shown when nothing left the device. Never silently swallowed: a report the user
    /// believes they filed and did not is worse than a visible failure.</summary>
    public const string FailedMessage = "Couldn't send diagnostics. Check your connection and try again.";

    private readonly DiagnosticsLogBuffer _buffer;
    private readonly DiagnosticsSessionLog _sessionLog;
    private readonly IDiagnosticsSink _sink;
    private readonly IDialogService _dialogs;
    private readonly IUserFeedback _feedback;
    private readonly IAppEnvironment _environment;
    private readonly IAuthService _authService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DiagnosticsReportCoordinator> _logger;

    public DiagnosticsReportCoordinator(
        DiagnosticsLogBuffer buffer,
        DiagnosticsSessionLog sessionLog,
        IDiagnosticsSink sink,
        IDialogService dialogs,
        IUserFeedback feedback,
        IAppEnvironment environment,
        IAuthService authService,
        TimeProvider timeProvider,
        ILogger<DiagnosticsReportCoordinator> logger)
    {
        _buffer = buffer;
        _sessionLog = sessionLog;
        _sink = sink;
        _dialogs = dialogs;
        _feedback = feedback;
        _environment = environment;
        _authService = authService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// True while a report is being assembled or sent. Guards against a double tap producing two
    /// events for one intent — the send is slow enough (it waits for the Sentry flush) that a second
    /// press is easy.
    /// </summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Runs the flow. Returns <c>true</c> only when a report actually reached Sentry — cancelling at
    /// the popup returns <c>false</c>, as does a failed send, so a caller that disables its button
    /// after a successful report does not disable it on a cancel.
    /// </summary>
    public async Task<bool> ReportAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            var choice = await _dialogs.ShowDiagnosticsReportAsync(DisclosureSummary).ConfigureAwait(true);
            if (choice is null)
            {
                _logger.LogInformation("Diagnostic report cancelled at the disclosure.");
                return false;
            }

            // Snapshotted only after consent. Collecting first and discarding on cancel would work
            // just as well technically, and would make the disclosure a formality rather than a gate.
            var current = _buffer.Snapshot();
            var previous = _sessionLog.Load();

            var report = DiagnosticsReportBuilder.Build(
                await BuildContextAsync(cancellationToken).ConfigureAwait(true),
                _timeProvider.GetUtcNow(),
                previous,
                current,
                choice.Value.Description);

            var sent = await _sink.SendAsync(report, choice.Value.Description, cancellationToken).ConfigureAwait(true);

            if (sent)
            {
                // Only once it is genuinely gone. Clearing on a failed send would throw away the
                // previous session's lines — the ones a crash flush existed to preserve — and the
                // retry would carry less than the attempt that failed.
                _sessionLog.Clear();

                // Confirmed branch only, matching how the other repeatable user actions breadcrumb.
                // Its value is on *later* events: "this user had already reported something" is real
                // context, and a cancel breadcrumb would only add buffer noise.
                SentrySdk.AddBreadcrumb("Diagnostic report sent", "state", "user");
            }

            await _feedback.ShowSnackbarAsync(sent ? SentMessage : FailedMessage).ConfigureAwait(true);
            return sent;
        }
        catch (Exception ex)
        {
            // This runs off a button the user pressed because something was already wrong. The
            // reporting path throwing on top of that is the worst outcome available, so it is caught
            // here and told to the user rather than raised.
            _logger.LogError(ex, "Diagnostic report flow failed.");
            await _feedback.ShowSnackbarAsync(FailedMessage).ConfigureAwait(true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<DiagnosticsContext> BuildContextAsync(CancellationToken cancellationToken)
    {
        var isSignedIn = false;
        try
        {
            isSignedIn = !string.IsNullOrWhiteSpace(
                await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A header field is not worth losing the report over — secure storage can fail, and if it
            // just did, that is itself something the log will show.
            _logger.LogWarning(ex, "Could not determine sign-in state for the diagnostic report.");
        }

        return new DiagnosticsContext(
            _environment.AppVersion,
            _environment.BuildConfiguration,
            _environment.Device,
            _environment.OsVersion,
            isSignedIn);
    }
}
