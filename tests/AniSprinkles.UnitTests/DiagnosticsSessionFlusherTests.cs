using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniSprinkles.UnitTests;

/// <summary>
/// When the diagnostics ring reaches disk, and when that file may be thrown away again (#112).
/// <para>
/// The ownership rule is the reason this lives in Core rather than in the Android activity that
/// drives it. Get it wrong in one direction and every line is sent twice; get it wrong in the other
/// and the crash a user is about to report is deleted on the launch that follows it. Neither would
/// be visible from the outside, and neither would be reachable by a test from the app project.
/// </para>
/// </summary>
public sealed class DiagnosticsSessionFlusherTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly List<string> _directories = [];

    public void Dispose()
    {
        foreach (var directory in _directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The temp dir is disposable; a leaked handle must not fail a passing run.
            }
        }
    }

    private (DiagnosticsSessionFlusher Flusher, DiagnosticsLogBuffer Buffer, DiagnosticsSessionLog Log) NewFlusher()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anisprinkles-flushertests", Guid.NewGuid().ToString("N"));
        _directories.Add(directory);

        var buffer = new DiagnosticsLogBuffer(new ManualTimeProvider(Start));
        var log = new DiagnosticsSessionLog(
            Path.Combine(directory, "previous-session.log"),
            NullLogger<DiagnosticsSessionLog>.Instance);

        return (new DiagnosticsSessionFlusher(buffer, log), buffer, log);
    }

    // ── Flushing ─────────────────────────────────────────────────────

    [Fact]
    public void FlushingWritesTheRingToDisk()
    {
        var (flusher, buffer, log) = NewFlusher();
        buffer.CreateLogger("Nav").LogInformation("NAVTRACE load start (manga 42)");

        Assert.True(flusher.Flush());

        Assert.Contains(log.Load(), line => line.Contains("NAVTRACE load start (manga 42)"));
    }

    [Fact]
    public void FlushingAnEmptyRingWritesNothing()
    {
        // A pause with nothing recorded is the common case on a quiet app; it must not count as a
        // flush, or the next resume would "clear" a file that belongs to an earlier process.
        var (flusher, _, log) = NewFlusher();

        Assert.False(flusher.Flush());

        Assert.Empty(log.Load());
    }

    // ── The ownership rule ───────────────────────────────────────────

    [Fact]
    public void ResumingAfterOurOwnFlushClearsIt()
    {
        // We are still alive, so the file was not needed — and the ring still holds every one of
        // those lines. Keeping it would put them into the next report twice.
        var (flusher, buffer, log) = NewFlusher();
        buffer.CreateLogger("Nav").LogInformation("still in the ring");
        flusher.Flush();

        Assert.True(flusher.ClearIfOwnedByThisProcess());

        Assert.Empty(log.Load());
    }

    [Fact]
    public void ResumingDoesNotTouchAFileLeftByAPreviousProcess()
    {
        // The crash case, and the one that matters most: the app died holding the ring, this file is
        // all that survived it, and the very next thing that happens is the launch on which the user
        // goes to report. Clearing here would delete the evidence before they could send it.
        var (flusher, _, log) = NewFlusher();
        log.Save(["the run that crashed"]);

        Assert.False(flusher.ClearIfOwnedByThisProcess());

        Assert.Equal(["the run that crashed"], log.Load());
    }

    [Fact]
    public void ResumingTwiceOnlyClearsOnce()
    {
        // Ownership is consumed by the clear. A second resume with no intervening flush must behave
        // like the previous-process case, not re-clear.
        var (flusher, buffer, log) = NewFlusher();
        buffer.CreateLogger("Nav").LogInformation("something");
        flusher.Flush();
        flusher.ClearIfOwnedByThisProcess();

        log.Save(["written by someone else"]);

        Assert.False(flusher.ClearIfOwnedByThisProcess());
        Assert.Equal(["written by someone else"], log.Load());
    }

    [Fact]
    public void AFailedFlushDoesNotClaimOwnership()
    {
        // Otherwise an empty pause would let the following resume delete a previous process's crash
        // flush — the exact file this whole mechanism exists to preserve.
        var (flusher, _, log) = NewFlusher();
        log.Save(["the run that crashed"]);

        flusher.Flush(); // ring is empty, so this writes nothing

        Assert.False(flusher.ClearIfOwnedByThisProcess());
        Assert.Equal(["the run that crashed"], log.Load());
    }

    [Fact]
    public void PauseResumePauseKeepsTheLatestFlush()
    {
        // The realistic lifecycle. Each pause overwrites, each resume clears its own — and the file
        // that survives is always the one from the most recent pause.
        var (flusher, buffer, log) = NewFlusher();
        var logger = buffer.CreateLogger("Nav");

        logger.LogInformation("first stretch");
        flusher.Flush();
        flusher.ClearIfOwnedByThisProcess();

        logger.LogInformation("second stretch");
        flusher.Flush();

        var lines = log.Load();
        Assert.Contains(lines, line => line.Contains("second stretch"));
    }
}
