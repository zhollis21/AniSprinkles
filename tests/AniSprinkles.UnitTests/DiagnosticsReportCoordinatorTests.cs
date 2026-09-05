using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The send-diagnostics flow (#112), shared by the Settings card and the Report action on an error
/// card so both behave identically.
/// <para>
/// Two things here are about consent rather than mechanics, and they are the ones worth having: the
/// user is told what will be collected <i>before</i> anything is collected, and cancelling sends
/// nothing. Sentry is silent to the sender, so the popup and the confirmation are the only evidence
/// the user ever gets that their report exists — which makes a wrongly-cheerful confirmation a real
/// failure, not a cosmetic one.
/// </para>
/// </summary>
public class DiagnosticsReportCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness : IDisposable
    {
        private readonly string _directory;

        public Harness()
        {
            _directory = Path.Combine(Path.GetTempPath(), "anisprinkles-reporttests", Guid.NewGuid().ToString("N"));
            Clock = new ManualTimeProvider(Now);
            Buffer = new DiagnosticsLogBuffer(Clock);
            SessionLog = new DiagnosticsSessionLog(
                Path.Combine(_directory, "previous-session.log"),
                NullLogger<DiagnosticsSessionLog>.Instance);
            Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("token"));

            Coordinator = new DiagnosticsReportCoordinator(
                Buffer, SessionLog, Sink, Dialogs, Feedback, Environment, Auth,
                Clock, NullLogger<DiagnosticsReportCoordinator>.Instance);
        }

        public ManualTimeProvider Clock { get; }

        public DiagnosticsLogBuffer Buffer { get; }

        public DiagnosticsSessionLog SessionLog { get; }

        public RecordingDiagnosticsSink Sink { get; } = new();

        public ScriptedDialogService Dialogs { get; } = new();

        public RecordingUserFeedback Feedback { get; } = new();

        public StubAppEnvironment Environment { get; } = new();

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();


        public DiagnosticsReportCoordinator Coordinator { get; }

        /// <summary>The user pressed Send, optionally with a note.</summary>
        public void UserSends(string? description = null)
            => Dialogs.DiagnosticsReportAnswer = new DiagnosticsReportChoice(description);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // The temp dir is disposable; a leaked handle must not fail a passing run.
            }
        }
    }

    // ── Consent comes first ──────────────────────────────────────────

    [Fact]
    public async Task TheDisclosureIsShownBeforeAnythingIsSent()
    {
        using var harness = new Harness();
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Contains(nameof(IDialogService.ShowDiagnosticsReportAsync), harness.Dialogs.Calls);
    }

    [Fact]
    public async Task TheDisclosureStatesWhatWillBeCollected()
    {
        // The popup's promise and the code that fulfils it live in one file precisely so this can be
        // asserted. A disclosure that drifts from reality is worse than none, because it is trusted.
        using var harness = new Harness();
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DiagnosticsReportCoordinator.DisclosureSummary, harness.Dialogs.LastDiagnosticsSummary);
    }

    [Fact]
    public async Task CancellingAtTheDisclosureSendsNothing()
    {
        using var harness = new Harness();
        harness.Buffer.CreateLogger("Nav").LogInformation("something private");
        // DiagnosticsReportAnswer defaults to null — the user dismissed the sheet.

        var sent = await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Empty(harness.Sink.Reports);
    }

    [Fact]
    public async Task CancellingShowsNoConfirmation()
    {
        // A "sent" snackbar after a cancel would be actively misleading about what left the device.
        using var harness = new Harness();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Empty(harness.Feedback.Snackbars);
    }

    [Fact]
    public async Task NothingIsCollectedUntilTheUserHasConsented()
    {
        // Collecting first and discarding on cancel would work identically from the outside, and
        // would make the disclosure a formality rather than a gate. Asserted by checking the sink is
        // still untouched at the moment the sheet is on screen.
        using var harness = new Harness();
        harness.UserSends();
        var sinkWasUntouchedAtDisclosure = false;
        harness.Dialogs.BeforeDiagnosticsReportAsync = () =>
        {
            sinkWasUntouchedAtDisclosure = harness.Sink.Reports.Count == 0;
            return Task.CompletedTask;
        };

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.True(sinkWasUntouchedAtDisclosure);
    }

    // ── The happy path ───────────────────────────────────────────────

    [Fact]
    public async Task SendingDeliversTheRingContents()
    {
        using var harness = new Harness();
        harness.Buffer.CreateLogger("Nav").LogInformation("NAVTRACE load start (manga 42)");
        harness.UserSends();

        var sent = await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        var report = Assert.Single(harness.Sink.Reports);
        Assert.Contains("NAVTRACE load start (manga 42)", report);
    }

    [Fact]
    public async Task SendingIncludesAFlushedPreviousSession()
    {
        // The crash case: the process died, the ring went with it, and this file is all that is left
        // of the run-up.
        using var harness = new Harness();
        harness.SessionLog.Save(["the run before the crash"]);
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Contains("the run before the crash", Assert.Single(harness.Sink.Reports));
    }

    [Fact]
    public async Task TheUsersDescriptionReachesBothTheReportAndTheSink()
    {
        // Two routes on purpose: into the attachment so the file is self-contained, and alongside it
        // so the sink can file it as Sentry user feedback linked to the event.
        using var harness = new Harness();
        harness.UserSends("I kept tapping manga and they all failed");

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Contains("I kept tapping manga", Assert.Single(harness.Sink.Reports));
        Assert.Equal("I kept tapping manga and they all failed", harness.Sink.LastDescription);
    }

    [Fact]
    public async Task SendingWithNoDescriptionIsAllowed()
    {
        // Requiring text would be friction for someone who only wants the log attached.
        using var harness = new Harness();
        harness.UserSends(description: null);

        Assert.True(await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheReportCarriesTheEnvironmentHeader()
    {
        using var harness = new Harness();
        harness.Environment.AppVersion = "9.9.9 (99)";
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Contains("9.9.9 (99)", Assert.Single(harness.Sink.Reports));
    }

    [Fact]
    public async Task TheReportSaysWhetherTheUserWasSignedIn()
    {
        using var harness = new Harness();
        harness.Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Contains("signed in: no", Assert.Single(harness.Sink.Reports));
    }

    [Fact]
    public async Task ASuccessfulSendIsConfirmedInWordsThatMatchTheDisclosure()
    {
        // Sentry says nothing back to the sender, so this snackbar is the only evidence the user
        // gets. Naming the same window the popup promised is what lets the two read back against
        // each other rather than the user taking "sent" on faith.
        using var harness = new Harness();
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DiagnosticsReportCoordinator.SentMessage, Assert.Single(harness.Feedback.Snackbars));
    }

    // ── When the send does not land ──────────────────────────────────

    [Fact]
    public async Task AFailedSendIsReportedAsAFailure()
    {
        // Telling the user their report was filed when nothing left the device is the worst outcome
        // this flow has: they stop chasing the problem and nobody ever sees it.
        using var harness = new Harness();
        harness.Sink.Result = false;
        harness.UserSends();

        var sent = await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Equal(DiagnosticsReportCoordinator.FailedMessage, Assert.Single(harness.Feedback.Snackbars));
    }

    [Fact]
    public async Task AFailedSendKeepsThePreviousSessionForTheRetry()
    {
        // Clearing on failure would throw away the crash flush — the thing the whole disk fallback
        // exists to preserve — and the retry would carry less than the attempt that failed.
        using var harness = new Harness();
        harness.SessionLog.Save(["the crash"]);
        harness.Sink.Result = false;
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["the crash"], harness.SessionLog.Load());
    }

    [Fact]
    public async Task ASuccessfulSendClearsThePreviousSession()
    {
        // Otherwise the same crash rides along on every report the user ever files afterwards.
        using var harness = new Harness();
        harness.SessionLog.Save(["the crash"]);
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.Empty(harness.SessionLog.Load());
    }

    [Fact]
    public async Task AThrowingSinkIsReportedToTheUserRatherThanRaised()
    {
        // This runs off a button pressed because something was already wrong. The reporting path
        // throwing on top of that is the worst failure available.
        using var harness = new Harness();
        harness.Sink.Throws = new InvalidOperationException("transport exploded");
        harness.UserSends();

        var sent = await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Equal(DiagnosticsReportCoordinator.FailedMessage, Assert.Single(harness.Feedback.Snackbars));
    }

    [Fact]
    public async Task AFailingAuthCheckDoesNotLoseTheReport()
    {
        // Secure storage can fail — and if it just did, that is itself something the log will show.
        // A header field is not worth dropping the report over.
        using var harness = new Harness();
        harness.Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException("keystore unavailable"));
        harness.UserSends();

        Assert.True(await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken));
    }

    // ── Double-tap ───────────────────────────────────────────────────

    [Fact]
    public async Task ASecondReportWhileTheFirstIsStillRunningIsIgnored()
    {
        // The send waits on a network flush, so a second press is easy — and would file two events
        // for one intent.
        using var harness = new Harness();
        harness.UserSends();
        var second = Task.FromResult(false);
        harness.Dialogs.BeforeDiagnosticsReportAsync = async () =>
        {
            second = harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);
            await Task.Yield();
        };

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.False(await second);
        Assert.Single(harness.Sink.Reports);
    }

    [Fact]
    public async Task IsBusyIsClearedAfterAFailureSoTheUserCanTryAgain()
    {
        // A guard that latches on would leave the button permanently dead after one bad network
        // moment.
        using var harness = new Harness();
        harness.Sink.Throws = new InvalidOperationException("boom");
        harness.UserSends();

        await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken);

        Assert.False(harness.Coordinator.IsBusy);
    }

    [Fact]
    public async Task ReportingTwiceInSequenceIsAllowed()
    {
        // Settings is not a one-shot: a user may hit two different problems in one session.
        using var harness = new Harness();
        harness.UserSends();

        Assert.True(await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken));
        Assert.True(await harness.Coordinator.ReportAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, harness.Sink.Reports.Count);
    }
}
