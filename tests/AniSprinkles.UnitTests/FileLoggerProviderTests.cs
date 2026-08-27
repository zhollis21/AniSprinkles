using Microsoft.Extensions.Logging;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 2 for <see cref="FileLoggerProvider"/> — the on-device log sink behind
/// <c>{AppDataDirectory}/logs/</c>. Nothing checked the rotation maths, which is the part that
/// decides whether a diagnostic report contains the crash or only the minutes after it.
/// <para>
/// Writes go through a bounded channel drained by a background task, so every assertion here runs
/// after <c>Dispose</c>, which is the flush point. That it <i>is</i> a flush point is itself one of
/// the tests: it was not before this pass.
/// </para>
/// </summary>
public sealed class FileLoggerProviderTests : IDisposable
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

    /// <summary>A private directory per test, so the suite stays parallel-safe.</summary>
    private string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "anisprinkles-logtests", Guid.NewGuid().ToString("N"));
        _directories.Add(path);
        return path;
    }

    private static string[] LinesIn(string directory, string file = "anisprinkles.log")
    {
        var path = Path.Combine(directory, file);
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    // ── Shutdown ─────────────────────────────────────────────────────

    [Fact]
    public void Dispose_FlushesWhateverIsStillQueued()
    {
        // The reason this matters: the lines most worth having are the ones written just before the
        // app goes away, and Dispose used to cancel the drain rather than wait for it — so a crash
        // or a backgrounded app dropped exactly the tail the report needed.
        var directory = NewDirectory();
        var provider = new FileLoggerProvider(directory);
        var logger = provider.CreateLogger("Shutdown");

        for (var i = 0; i < 200; i++)
        {
            logger.LogInformation("queued line {Index}", i);
        }

        provider.Dispose();

        var lines = LinesIn(directory);
        Assert.Equal(200, lines.Count(l => l.Contains("queued line")));
    }

    [Fact]
    public void DisposingTwice_IsHarmless()
    {
        var provider = new FileLoggerProvider(NewDirectory());

        provider.Dispose();
        provider.Dispose();
    }

    [Fact]
    public void CreatingALoggerAfterDispose_Throws()
    {
        // A caller still holding the provider past shutdown is a lifetime bug worth surfacing,
        // not something to paper over with a silent no-op logger.
        var provider = new FileLoggerProvider(NewDirectory());
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.CreateLogger("Late"));
    }

    // ── The file itself ──────────────────────────────────────────────

    [Fact]
    public void TheLogDirectoryIsCreatedIfItDoesNotExist()
    {
        // First run on a fresh install: nothing has made {AppDataDirectory}/logs/ yet.
        var directory = NewDirectory();
        Assert.False(Directory.Exists(directory));

        using (var provider = new FileLoggerProvider(directory))
        {
            provider.CreateLogger("Boot").LogWarning("hello");
        }

        Assert.True(File.Exists(Path.Combine(directory, "anisprinkles.log")));
    }

    [Fact]
    public void TheFileNameIsConfigurable()
    {
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory, fileName: "custom.log"))
        {
            provider.CreateLogger("Boot").LogWarning("hello");
        }

        Assert.True(File.Exists(Path.Combine(directory, "custom.log")));
    }

    [Fact]
    public void ALineCarriesItsLevelCategoryAndMessage()
    {
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory))
        {
            provider.CreateLogger("AniSprinkles.Nav").LogWarning("NAVTRACE something happened");
        }

        var line = Assert.Single(LinesIn(directory), l => l.Contains("NAVTRACE"));
        Assert.Contains("[Warning]", line);
        Assert.Contains("AniSprinkles.Nav", line);
    }

    [Fact]
    public void AnExceptionIsAppendedToItsLine()
    {
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory))
        {
            provider.CreateLogger("Api").LogError(new InvalidOperationException("boom"), "call failed");
        }

        var line = Assert.Single(LinesIn(directory), l => l.Contains("call failed"));
        Assert.Contains("boom", line);
    }

    [Fact]
    public void AMultiLineMessage_IsFlattenedOntoOneLine()
    {
        // The format is one record per line. A message carrying newlines through would break every
        // reader that splits on them, including a human scrolling the file.
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory))
        {
            provider.CreateLogger("Api").LogWarning(
                "first{NewLine}second", Environment.NewLine);
        }

        Assert.Single(LinesIn(directory), l => l.Contains("first") && l.Contains("second"));
    }

    // ── Level filtering ──────────────────────────────────────────────

    [Fact]
    public void MessagesBelowTheMinimumLevel_AreDropped()
    {
        // Release keeps only Warning and above; a Debug-level trace must not reach the file.
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory, minimumLevel: LogLevel.Warning))
        {
            var logger = provider.CreateLogger("Chatty");
            logger.LogInformation("informational noise");
            logger.LogWarning("worth keeping");
        }

        var lines = LinesIn(directory);
        Assert.DoesNotContain(lines, l => l.Contains("informational noise"));
        Assert.Contains(lines, l => l.Contains("worth keeping"));
    }

    [Fact]
    public void LogLevelNone_IsNeverEnabled()
    {
        using var provider = new FileLoggerProvider(NewDirectory(), minimumLevel: LogLevel.Trace);

        var logger = provider.CreateLogger("Any");

        Assert.True(logger.IsEnabled(LogLevel.Trace));
        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void BeginScope_ReturnsSomethingDisposable()
    {
        // Scopes are not supported, but ILogger callers dispose the handle unconditionally.
        using var provider = new FileLoggerProvider(NewDirectory());

        using var scope = provider.CreateLogger("Any").BeginScope("state");

        Assert.NotNull(scope);
    }

    // ── Rotation and retention ───────────────────────────────────────

    [Fact]
    public void CrossingTheSizeCap_MovesTheCurrentFileToArchiveOne()
    {
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory, maxFileSizeBytes: 256))
        {
            var logger = provider.CreateLogger("Bulk");
            for (var i = 0; i < 20; i++)
            {
                logger.LogWarning("padding padding padding padding {Index}", i);
            }
        }

        Assert.True(File.Exists(Path.Combine(directory, "anisprinkles.log")));
        Assert.True(File.Exists(Path.Combine(directory, "anisprinkles.log.1")));
    }

    [Fact]
    public void FurtherRotations_ShiftTheOlderArchivesDown()
    {
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory, maxFileSizeBytes: 256))
        {
            var logger = provider.CreateLogger("Bulk");
            for (var i = 0; i < 60; i++)
            {
                logger.LogWarning("padding padding padding padding {Index}", i);
            }
        }

        Assert.True(File.Exists(Path.Combine(directory, "anisprinkles.log.1")));
        Assert.True(File.Exists(Path.Combine(directory, "anisprinkles.log.2")));
    }

    [Fact]
    public void RetentionCapsHowManyArchivesSurvive()
    {
        // The point of the cap: a chatty session must not fill the user's storage.
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory, maxFileSizeBytes: 256, retainedFiles: 2))
        {
            var logger = provider.CreateLogger("Bulk");
            for (var i = 0; i < 200; i++)
            {
                logger.LogWarning("padding padding padding padding {Index}", i);
            }
        }

        Assert.True(File.Exists(Path.Combine(directory, "anisprinkles.log.2")));
        Assert.False(File.Exists(Path.Combine(directory, "anisprinkles.log.3")));
    }

    [Fact]
    public void ARetentionOfZero_IsClampedToKeepingOneArchive()
    {
        // Math that produced a zero-length loop would otherwise leave the current file unrotated
        // and growing without bound.
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory, maxFileSizeBytes: 256, retainedFiles: 0))
        {
            var logger = provider.CreateLogger("Bulk");
            for (var i = 0; i < 60; i++)
            {
                logger.LogWarning("padding padding padding padding {Index}", i);
            }
        }

        Assert.True(File.Exists(Path.Combine(directory, "anisprinkles.log.1")));
        Assert.False(File.Exists(Path.Combine(directory, "anisprinkles.log.2")));
    }

    [Fact]
    public void TheNewestLinesLiveInTheCurrentFile()
    {
        // Rotation must not leave the tail in an archive — a reader opening the log expects the
        // most recent activity there.
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory, maxFileSizeBytes: 256))
        {
            var logger = provider.CreateLogger("Bulk");
            for (var i = 0; i < 60; i++)
            {
                logger.LogWarning("padding padding padding padding {Index}", i);
            }

            logger.LogWarning("the very last thing");
        }

        Assert.Contains(LinesIn(directory), l => l.Contains("the very last thing"));
    }

    [Fact]
    public void UnderTheSizeCap_NothingRotates()
    {
        var directory = NewDirectory();

        using (var provider = new FileLoggerProvider(directory, maxFileSizeBytes: 1024 * 1024))
        {
            provider.CreateLogger("Quiet").LogWarning("one small line");
        }

        Assert.False(File.Exists(Path.Combine(directory, "anisprinkles.log.1")));
    }
}
