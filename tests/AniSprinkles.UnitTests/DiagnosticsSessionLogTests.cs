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

    [Test]
    public void SavedLinesComeBackInOrder()
    {
        var log = NewLog(out _);

        Assert.That(log.Save(["first", "second", "third"]), Is.True);

        Assert.That(log.Load(), Is.EqualTo(["first", "second", "third"]));
    }

    [Test]
    public void TheDirectoryIsCreatedIfItDoesNotExistYet()
    {
        // First run on a fresh install, or a crash before anything else has touched the log folder.
        var log = NewLog(out var filePath);
        Assert.That(Directory.Exists(Path.GetDirectoryName(filePath)), Is.False);

        Assert.That(log.Save(["something"]), Is.True);

        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public void SavingAgainReplacesTheFileRatherThanAppending()
    {
        // One session, not a growing history. Appending would make this file unbounded, which is the
        // one thing the in-memory design exists to avoid.
        var log = NewLog(out _);
        log.Save(["old session"]);

        log.Save(["new session"]);

        Assert.That(log.Load(), Is.EqualTo(["new session"]));
    }

    // ── Nothing to save ──────────────────────────────────────────────

    [Test]
    public void SavingNothingReportsFailureRatherThanWritingAnEmptyFile()
    {
        var log = NewLog(out var filePath);

        Assert.That(log.Save([]), Is.False);

        Assert.That(File.Exists(filePath), Is.False);
    }

    [Test]
    public void SavingNothingLeavesAnEarlierFlushIntact()
    {
        // The case this protects: the app crashes and flushes, then the user reopens it and pauses it
        // again before doing anything. That second pause has an empty ring, and truncating on it
        // would erase the crash — the only flush that mattered.
        var log = NewLog(out _);
        log.Save(["the crash"]);

        log.Save([]);

        Assert.That(log.Load(), Is.EqualTo(["the crash"]));
    }

    // ── Nothing to load ──────────────────────────────────────────────

    [Test]
    public void LoadingWithNoFileReturnsNothingRatherThanThrowing()
    {
        // The normal case, every clean run: nothing crashed, so nothing was flushed.
        var log = NewLog(out _);

        Assert.That(log.Load(), Is.Empty);
    }

    [Test]
    public void LoadingFromAMissingDirectoryReturnsNothing()
    {
        var log = new DiagnosticsSessionLog(
            Path.Combine(NewDirectory(), "nested", "deeper", "previous-session.log"),
            NullLogger<DiagnosticsSessionLog>.Instance);

        Assert.That(log.Load(), Is.Empty);
    }

    // ── Clearing ─────────────────────────────────────────────────────

    [Test]
    public void ClearRemovesTheFile()
    {
        // Called once a report has been sent, so the same session is not attached to every later
        // report the user files.
        var log = NewLog(out var filePath);
        log.Save(["sent already"]);

        log.Clear();

        Assert.That(File.Exists(filePath), Is.False);
        Assert.That(log.Load(), Is.Empty);
    }

    [Test]
    public void ClearingWhenThereIsNoFileIsHarmless()
    {
        var log = NewLog(out _);

        log.Clear();
        log.Clear();
    }

    // ── Failure is never fatal ───────────────────────────────────────

    [Test]
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

        Assert.That(log.Save(["anything"]), Is.False);
    }

    [Test]
    public void LoadFailingReturnsNothingInsteadOfThrowing()
    {
        var directory = NewDirectory();
        var filePath = Path.Combine(directory, "previous-session.log");
        Directory.CreateDirectory(filePath);
        var log = new DiagnosticsSessionLog(filePath, NullLogger<DiagnosticsSessionLog>.Instance);

        Assert.That(log.Load(), Is.Empty);
    }

    [Test]
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
