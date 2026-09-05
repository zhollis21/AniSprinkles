using Microsoft.Extensions.Logging.Abstractions;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The one file the diagnostics ring ever writes (#112) — a snapshot taken when the process is about
/// to lose it.
/// <para>
/// The behaviour worth guarding is what happens when things go wrong: a save that finds nothing to
/// write, a load with no file, a directory that does not exist yet. Every one of those runs from a
/// global exception handler or an activity pause, where a throw would turn a diagnostic convenience
/// into the crash it was supposed to explain.
/// </para>
/// </summary>
public sealed class DiagnosticsSessionLogTests : IDisposable
{
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
                // A leaked handle must not fail an otherwise-passing run; the temp dir is disposable.
            }
        }
    }

    private string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "anisprinkles-sessiontests", Guid.NewGuid().ToString("N"));
        _directories.Add(path);
        return path;
    }

    private DiagnosticsSessionLog NewLog(out string filePath)
    {
        filePath = Path.Combine(NewDirectory(), "previous-session.log");
        return new DiagnosticsSessionLog(filePath, NullLogger<DiagnosticsSessionLog>.Instance);
    }

    // ── Round trip ───────────────────────────────────────────────────

    [Fact]
    public void SavedLinesComeBackInOrder()
    {
        var log = NewLog(out _);

        Assert.True(log.Save(["first", "second", "third"]));

        Assert.Equal(["first", "second", "third"], log.Load());
    }

    [Fact]
    public void TheDirectoryIsCreatedIfItDoesNotExistYet()
    {
        // First run on a fresh install, or a crash before anything else has touched the log folder.
        var log = NewLog(out var filePath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(filePath)));

        Assert.True(log.Save(["something"]));

        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void SavingAgainReplacesTheFileRatherThanAppending()
    {
        // One session, not a growing history. Appending would make this file unbounded, which is the
        // one thing the in-memory design exists to avoid.
        var log = NewLog(out _);
        log.Save(["old session"]);

        log.Save(["new session"]);

        Assert.Equal(["new session"], log.Load());
    }

    // ── Nothing to save ──────────────────────────────────────────────

    [Fact]
    public void SavingNothingReportsFailureRatherThanWritingAnEmptyFile()
    {
        var log = NewLog(out var filePath);

        Assert.False(log.Save([]));

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void SavingNothingLeavesAnEarlierFlushIntact()
    {
        // The case this protects: the app crashes and flushes, then the user reopens it and pauses it
        // again before doing anything. That second pause has an empty ring, and truncating on it
        // would erase the crash — the only flush that mattered.
        var log = NewLog(out _);
        log.Save(["the crash"]);

        log.Save([]);

        Assert.Equal(["the crash"], log.Load());
    }

    // ── Nothing to load ──────────────────────────────────────────────

    [Fact]
    public void LoadingWithNoFileReturnsNothingRatherThanThrowing()
    {
        // The normal case, every clean run: nothing crashed, so nothing was flushed.
        var log = NewLog(out _);

        Assert.Empty(log.Load());
    }

    [Fact]
    public void LoadingFromAMissingDirectoryReturnsNothing()
    {
        var log = new DiagnosticsSessionLog(
            Path.Combine(NewDirectory(), "nested", "deeper", "previous-session.log"),
            NullLogger<DiagnosticsSessionLog>.Instance);

        Assert.Empty(log.Load());
    }

    // ── Clearing ─────────────────────────────────────────────────────

    [Fact]
    public void ClearRemovesTheFile()
    {
        // Called once a report has been sent, so the same session is not attached to every later
        // report the user files.
        var log = NewLog(out var filePath);
        log.Save(["sent already"]);

        log.Clear();

        Assert.False(File.Exists(filePath));
        Assert.Empty(log.Load());
    }

    [Fact]
    public void ClearingWhenThereIsNoFileIsHarmless()
    {
        var log = NewLog(out _);

        log.Clear();
        log.Clear();
    }

    // ── Failure is never fatal ───────────────────────────────────────

    [Fact]
    public void SaveFailingReportsFailureInsteadOfThrowing()
    {
        // Every caller is a global exception handler or an activity pause. An escaping exception
        // there is strictly worse than a lost log — it becomes the crash instead of explaining one.
        // A directory standing where the file should be is the cheapest reliable way to make the
        // write fail on every platform.
        var directory = NewDirectory();
        var filePath = Path.Combine(directory, "previous-session.log");
        Directory.CreateDirectory(filePath);
        var log = new DiagnosticsSessionLog(filePath, NullLogger<DiagnosticsSessionLog>.Instance);

        Assert.False(log.Save(["anything"]));
    }

    [Fact]
    public void LoadFailingReturnsNothingInsteadOfThrowing()
    {
        var directory = NewDirectory();
        var filePath = Path.Combine(directory, "previous-session.log");
        Directory.CreateDirectory(filePath);
        var log = new DiagnosticsSessionLog(filePath, NullLogger<DiagnosticsSessionLog>.Instance);

        Assert.Empty(log.Load());
    }

    [Fact]
    public void ClearFailingDoesNotThrow()
    {
        var directory = NewDirectory();
        var filePath = Path.Combine(directory, "previous-session.log");
        Directory.CreateDirectory(filePath);
        Directory.CreateDirectory(Path.Combine(filePath, "occupied"));
        var log = new DiagnosticsSessionLog(filePath, NullLogger<DiagnosticsSessionLog>.Instance);

        log.Clear();
    }
}
