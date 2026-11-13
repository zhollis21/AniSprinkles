namespace AniSprinkles.Services;

/// <summary>
/// Marks an event that crashed the app as <see cref="SentryLevel.Fatal"/> (#180).
/// <para>
/// Since <c>Sentry.Maui</c> 7.0.0, an exception escaping a background thread reaches Sentry as a
/// managed event under the <c>AppDomain.UnhandledException</c> mechanism — but at level <c>error</c>,
/// the same as every handled failure <see cref="ErrorReportService"/> records, so a crash could not
/// be told from a logged error by level.
/// </para>
/// <para>
/// "Terminal" is the SDK's own definition (<see cref="SentryEventExtensions.IsFromTerminalException"/>):
/// unhandled, and not explicitly marked non-terminal. That keeps unobserved task exceptions, which
/// the SDK marks non-terminal because the process carries on, at their own level. It also promotes
/// UI-thread exceptions that <c>MainActivity</c>'s <c>UnhandledExceptionRaiser</c> keeps alive with
/// <c>Handled = true</c>: the SDK counts those as terminal, and they are crashes in all but name (#156).
/// </para>
/// </summary>
public static class SentryCrashSeverity
{
    /// <summary>
    /// Raises a terminal event to <see cref="SentryLevel.Fatal"/> in place and returns it, shaped for
    /// <c>options.SetBeforeSend</c>. Every other event passes through unchanged.
    /// </summary>
    public static SentryEvent Apply(SentryEvent evt)
    {
        if (evt.IsFromTerminalException())
        {
            evt.Level = SentryLevel.Fatal;
        }

        return evt;
    }
}
