using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The in-memory ring behind the send-diagnostics button (#112). What is asserted here is the two
/// bounds — age and count — because they are the entire contract: the window a report promises the
/// user, and the memory an install that never reports anything still pays.
/// <para>
/// Every time-dependent test drives <see cref="ManualTimeProvider"/> rather than sleeping, so the
/// five-minute retention is exercised in microseconds and the suite stays deterministic.
/// </para>
/// </summary>
public class DiagnosticsLogBufferTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static (DiagnosticsLogBuffer Buffer, ManualTimeProvider Clock) NewBuffer(
        TimeSpan? retention = null,
        int maxEntries = DiagnosticsLogBuffer.DefaultMaxEntries,
        LogLevel minimumLevel = LogLevel.Information)
    {
        var clock = new ManualTimeProvider(Start);
        return (new DiagnosticsLogBuffer(clock, retention, maxEntries, minimumLevel), clock);
    }

    // ── What a line carries ──────────────────────────────────────────

    [Fact]
    public void ALineCarriesItsTimestampLevelCategoryAndMessage()
    {
        var (buffer, _) = NewBuffer();

        buffer.CreateLogger("AniSprinkles.Nav").LogInformation("NAVTRACE load start (manga 42)");

        var line = Assert.Single(buffer.Snapshot());
        Assert.Contains("[Information]", line);
        Assert.Contains("AniSprinkles.Nav", line);
        Assert.Contains("NAVTRACE load start (manga 42)", line);
        Assert.Contains(Start.ToString("O"), line);
    }

    [Fact]
    public void AnExceptionIsAppendedToItsLine()
    {
        // The stack trace is most of what makes a report actionable; dropping it would leave the
        // reader with only the fact that something failed.
        var (buffer, _) = NewBuffer();

        buffer.CreateLogger("Api").LogError(new InvalidOperationException("boom"), "call failed");

        var line = Assert.Single(buffer.Snapshot());
        Assert.Contains("call failed", line);
        Assert.Contains("boom", line);
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("first\r\nsecond")]
    [InlineData("first\rsecond")]
    public void AMultiLineMessageIsFlattenedOntoOneLine(string message)
    {
        // One record per line is the contract every reader downstream depends on. All three newline
        // encodings are checked rather than Environment.NewLine: a report can carry text produced
        // elsewhere, and a single stray newline breaks the format for everyone.
        var (buffer, _) = NewBuffer();

        buffer.CreateLogger("Api").LogInformation("{Message}", message);

        var line = Assert.Single(buffer.Snapshot());
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Contains("first", line);
        Assert.Contains("second", line);
    }

    [Fact]
    public void EntriesComeBackOldestFirst()
    {
        // A report is read top to bottom as a narrative. Reversed, it is unreadable.
        var (buffer, clock) = NewBuffer();
        var logger = buffer.CreateLogger("Seq");

        logger.LogInformation("one");
        clock.Advance(TimeSpan.FromSeconds(1));
        logger.LogInformation("two");
        clock.Advance(TimeSpan.FromSeconds(1));
        logger.LogInformation("three");

        var lines = buffer.Snapshot();
        Assert.Equal(3, lines.Count);
        Assert.Contains("one", lines[0]);
        Assert.Contains("two", lines[1]);
        Assert.Contains("three", lines[2]);
    }

    // ── Level filtering ──────────────────────────────────────────────

    [Fact]
    public void MessagesBelowTheMinimumLevelAreDropped()
    {
        var (buffer, _) = NewBuffer(minimumLevel: LogLevel.Information);
        var logger = buffer.CreateLogger("Chatty");

        logger.LogDebug("debug noise");
        logger.LogInformation("worth keeping");

        var line = Assert.Single(buffer.Snapshot());
        Assert.Contains("worth keeping", line);
    }

    [Fact]
    public void InformationIsKept_WhichIsTheWholePointOfThisSink()
    {
        // The file log drops Information in Release, which is every trace worth having — no NAVTRACE,
        // no PageState, no HTTP. If this ever regresses to Warning, the feature is pointless.
        var (buffer, _) = NewBuffer();

        buffer.CreateLogger("Nav").LogInformation("NAVTRACE something");

        Assert.Single(buffer.Snapshot());
    }

    [Fact]
    public void LogLevelNoneIsNeverEnabled()
    {
        var (buffer, _) = NewBuffer(minimumLevel: LogLevel.Trace);

        var logger = buffer.CreateLogger("Any");

        Assert.True(logger.IsEnabled(LogLevel.Trace));
        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void BeginScopeReturnsSomethingDisposable()
    {
        // Scopes are not supported, but ILogger callers dispose the handle unconditionally.
        var (buffer, _) = NewBuffer();

        using var scope = buffer.CreateLogger("Any").BeginScope("state");

        Assert.NotNull(scope);
    }

    // ── The age bound ────────────────────────────────────────────────

    [Fact]
    public void EntriesOlderThanTheRetentionWindowAreDropped()
    {
        var (buffer, clock) = NewBuffer(retention: TimeSpan.FromMinutes(5));
        var logger = buffer.CreateLogger("Aging");

        logger.LogInformation("ancient history");
        clock.Advance(TimeSpan.FromMinutes(6));
        logger.LogInformation("recent");

        var line = Assert.Single(buffer.Snapshot());
        Assert.Contains("recent", line);
    }

    [Fact]
    public void AnEntryExactlyAtTheEdgeOfTheWindowIsKept()
    {
        // The boundary is inclusive: an entry exactly `retention` old is still inside the window the
        // popup promised the user, and dropping it would make the promise quietly false.
        var (buffer, clock) = NewBuffer(retention: TimeSpan.FromMinutes(5));

        buffer.CreateLogger("Edge").LogInformation("right on the line");
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Single(buffer.Snapshot());
    }

    [Fact]
    public void AgedOutEntriesAreDroppedOnSnapshotEvenWithNoFurtherWrites()
    {
        // The failure this guards: an app that goes quiet after a problem — which is exactly what a
        // user reporting one does — would otherwise hand over lines from well outside the window,
        // making the report say five minutes and mean an hour.
        var (buffer, clock) = NewBuffer(retention: TimeSpan.FromMinutes(5));

        buffer.CreateLogger("Quiet").LogInformation("long ago");
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void TheWindowSlidesRatherThanResetting()
    {
        var (buffer, clock) = NewBuffer(retention: TimeSpan.FromMinutes(5));
        var logger = buffer.CreateLogger("Sliding");

        for (var minute = 0; minute < 10; minute++)
        {
            logger.LogInformation("minute {Minute}", minute);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        // At t=10m the window covers (5m, 10m] — the five most recent entries.
        var lines = buffer.Snapshot();
        Assert.Equal(5, lines.Count);
        Assert.Contains("minute 5", lines[0]);
        Assert.Contains("minute 9", lines[^1]);
    }

    // ── The count bound ──────────────────────────────────────────────

    [Fact]
    public void TheRingNeverGrowsPastItsEntryCap()
    {
        // Retention alone is not a memory bound. A burst — a paged list, a retry storm — can emit
        // thousands of lines well inside the window, and without this cap the ring is unbounded in
        // exactly the situation a user is most likely to be reporting.
        var (buffer, _) = NewBuffer(maxEntries: 50);
        var logger = buffer.CreateLogger("Burst");

        for (var i = 0; i < 500; i++)
        {
            logger.LogInformation("line {Index}", i);
        }

        Assert.Equal(50, buffer.Snapshot().Count);
    }

    [Fact]
    public void WhenTheCapIsHitTheOldestLinesGoFirst()
    {
        // Dropping the newest would throw away the moment of failure and keep the run-up to it,
        // which is precisely backwards.
        var (buffer, _) = NewBuffer(maxEntries: 3);
        var logger = buffer.CreateLogger("Burst");

        for (var i = 0; i < 6; i++)
        {
            logger.LogInformation("line {Index}", i);
        }

        var lines = buffer.Snapshot();
        Assert.Contains("line 3", lines[0]);
        Assert.Contains("line 5", lines[^1]);
        Assert.DoesNotContain(lines, l => l.Contains("line 0"));
    }

    [Fact]
    public void ACapOfZeroIsClampedRatherThanDisablingTheBuffer()
    {
        // Guards a configuration mistake turning the feature into a silent no-op that still reports
        // success to the user.
        var (buffer, _) = NewBuffer(maxEntries: 0);

        buffer.CreateLogger("Any").LogInformation("something");

        Assert.Single(buffer.Snapshot());
    }

    // ── Snapshot semantics ───────────────────────────────────────────

    [Fact]
    public void ASnapshotIsNotAffectedByLaterWrites()
    {
        // The report is built from a snapshot while the app keeps logging — including the report
        // flow's own lines. A live view would let the report grow while it is being assembled.
        var (buffer, _) = NewBuffer();
        var logger = buffer.CreateLogger("Live");
        logger.LogInformation("before");

        var snapshot = buffer.Snapshot();
        logger.LogInformation("after");

        Assert.Single(snapshot);
    }

    [Fact]
    public void AnEmptyBufferSnapshotsToNothingRatherThanThrowing()
    {
        var (buffer, _) = NewBuffer();

        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void DisposeDoesNotThrowAndLeavesLoggersUsable()
    {
        // Unlike the file logger, there is nothing to release — and MAUI disposes providers at times
        // that are hard to predict, so a logger held past disposal must not start throwing.
        var (buffer, _) = NewBuffer();
        var logger = buffer.CreateLogger("Late");

        buffer.Dispose();
        logger.LogInformation("after dispose");

        Assert.Single(buffer.Snapshot());
    }

    // ── Concurrency ──────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentWritesFromManyThreadsDoNotLoseOrCorruptEntries()
    {
        // Logging happens on every thread this app uses — UI, HTTP continuations, the airing worker.
        // An unsynchronised ring would throw or silently drop under that, in the one code path whose
        // job is to explain what went wrong.
        var (buffer, _) = NewBuffer(maxEntries: 10_000);
        var logger = buffer.CreateLogger("Threads");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                logger.LogInformation("worker {Worker} line {Index}", worker, i);
            }
        })));

        Assert.Equal(2000, buffer.Snapshot().Count);
    }

    [Fact]
    public async Task SnapshottingWhileWritingIsSafe()
    {
        var (buffer, _) = NewBuffer(maxEntries: 10_000);
        var logger = buffer.CreateLogger("Threads");
        using var stop = new CancellationTokenSource();

        var writer = Task.Run(
            () =>
            {
                while (!stop.Token.IsCancellationRequested)
                {
                    logger.LogInformation("noise");
                }
            },
            TestContext.Current.CancellationToken);

        for (var i = 0; i < 200; i++)
        {
            _ = buffer.Snapshot();
        }

        await stop.CancelAsync();
        await writer;
    }
}
