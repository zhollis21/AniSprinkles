using Sentry.Protocol;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #180. A crash on a background thread reaches Sentry as a managed event at level <c>error</c>, the
/// same level as every handled failure <c>ErrorReportService</c> records, so a crash could not be told
/// from a logged error by level. These pin the promotion to <c>fatal</c> and, as importantly, every
/// kind of event it must leave alone.
/// </summary>
public class SentryCrashSeverityTests
{
    private static SentryEvent EventWith(Mechanism? mechanism, SentryLevel level = SentryLevel.Error) => new()
    {
        Level = level,
        SentryExceptions =
        [
            new SentryException
            {
                Type = "System.InvalidOperationException",
                Value = "boom",
                Mechanism = mechanism,
            }
        ],
    };

    private static SentryEvent UnhandledExceptionEvent(bool? terminal)
    {
        var exception = new InvalidOperationException("boom");
        exception.Data[Mechanism.HandledKey] = false;
        if (terminal is { } value)
        {
            exception.Data[Mechanism.TerminalKey] = value;
        }

        return new SentryEvent(exception) { Level = SentryLevel.Error };
    }

    [Test]
    public void Apply_PromotesAnUnhandledTerminalExceptionToFatal()
    {
        // What AppDomain.UnhandledException produces: handled false, terminal left unset, which the
        // SDK reads as terminal.
        var evt = EventWith(new Mechanism { Type = "AppDomain.UnhandledException", Handled = false });

        var result = SentryCrashSeverity.Apply(evt);

        Assert.That(result.Level, Is.EqualTo(SentryLevel.Fatal));
    }

    [Test]
    public void Apply_PromotesWhenTerminalIsExplicitlyTrue()
    {
        // Mechanism.Terminal has an internal setter; the SDK takes the flag from the exception's Data.
        var evt = UnhandledExceptionEvent(terminal: true);

        Assert.That(SentryCrashSeverity.Apply(evt).Level, Is.EqualTo(SentryLevel.Fatal));
    }

    [Test]
    public void Apply_PromotesWhenTheOriginalExceptionIsMarkedUnhandled()
    {
        // The SDK's other route to "terminal": the flag on the exception's Data rather than on a
        // SentryException's mechanism.
        var evt = UnhandledExceptionEvent(terminal: null);

        Assert.That(SentryCrashSeverity.Apply(evt).Level, Is.EqualTo(SentryLevel.Fatal));
    }

    [Test]
    public void Apply_LeavesAnUnhandledNonTerminalExceptionAlone()
    {
        // UnobservedTaskException marks its events terminal = false: the process carries on, so
        // calling it a crash would overstate it.
        var evt = UnhandledExceptionEvent(terminal: false);

        Assert.That(SentryCrashSeverity.Apply(evt).Level, Is.EqualTo(SentryLevel.Error));
    }

    [Test]
    public void Apply_LeavesAHandledExceptionAlone()
    {
        // ErrorReportService.Record's events — the bulk of what Sentry receives.
        var evt = EventWith(new Mechanism { Type = "SentryLogger", Handled = true });

        Assert.That(SentryCrashSeverity.Apply(evt).Level, Is.EqualTo(SentryLevel.Error));
    }

    [Test]
    public void Apply_LeavesAnExceptionWithNoMechanismAlone()
    {
        var evt = EventWith(mechanism: null);

        Assert.That(SentryCrashSeverity.Apply(evt).Level, Is.EqualTo(SentryLevel.Error));
    }

    [Test]
    public void Apply_DoesNotRaiseOrLowerAHandledEventsOwnLevel()
    {
        // Only a terminal crash moves; a handled warning stays a warning rather than being normalised.
        var evt = EventWith(new Mechanism { Handled = true }, SentryLevel.Warning);

        Assert.That(SentryCrashSeverity.Apply(evt).Level, Is.EqualTo(SentryLevel.Warning));
    }

    [Test]
    public void Apply_LeavesAnEventWithNoExceptionAlone()
    {
        var evt = new SentryEvent { Level = SentryLevel.Info, Message = "plain message" };

        Assert.That(SentryCrashSeverity.Apply(evt).Level, Is.EqualTo(SentryLevel.Info));
    }

    [Test]
    public void Apply_ReturnsTheSameInstance()
    {
        // Shaped for SetBeforeSend, where a null return drops the event: it must hand back what it was given.
        var evt = EventWith(new Mechanism { Handled = false });

        Assert.That(SentryCrashSeverity.Apply(evt), Is.SameAs(evt));
    }
}
