using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// The one file <see cref="DiagnosticsLogBuffer"/> ever writes: a snapshot of the ring taken when the
/// process is about to lose it (#112).
/// <para>
/// The ring is memory, so on its own it cannot answer "the app died, then I went to report it".
/// Android's three global handlers all keep the process alive — <c>UnhandledExceptionRaiser</c> sets
/// <c>Handled = true</c> and <c>UnobservedTaskException</c> calls <c>SetObserved</c> — so for the
/// failures this app actually produces the ring survives and this file is never needed. It exists for
/// the cases that do take the process with them: a native crash, an OOM kill, a force-close.
/// </para>
/// <para>
/// Exactly one file, overwritten each time, holding at most one session. Never appended to, so it
/// cannot grow — the ring's own bounds are the size limit.
/// </para>
/// <para>
/// Deliberately <b>not</b> cleared on sign-out, unlike <c>AppSettings</c>. A crash spanning a
/// sign-out and sign-in is exactly the kind of thing worth reporting, and this app is installed on
/// one person's phone — the case this would protect against, a second account signing in within the
/// retention window and sending the first one's activity, does not arise here. Worth revisiting
/// before any public release, along with the rest of the pre-release logging posture.
/// </para>
/// </summary>
public sealed class DiagnosticsSessionLog
{
    /// <summary>The header written above the restored lines, so a report reader can see the join.</summary>
    public const string PreviousSessionHeader = "── previous session ──";

    private readonly string _filePath;
    private readonly ILogger<DiagnosticsSessionLog> _logger;
    private readonly object _lock = new();

    public DiagnosticsSessionLog(string filePath, ILogger<DiagnosticsSessionLog> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>
    /// Writes <paramref name="lines"/> over whatever was there. Returns whether it landed.
    /// <para>
    /// Never throws. Every caller is either a global exception handler or an activity pause — places
    /// where an exception escaping would turn a diagnostic convenience into the crash it was meant to
    /// explain.
    /// </para>
    /// </summary>
    public bool Save(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            // Nothing to say. Deliberately leaves any existing file alone rather than truncating it:
            // an empty ring on a later pause must not erase the flush from the crash before it.
            return false;
        }

        try
        {
            lock (_lock)
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllLines(_filePath, lines);
            }

            return true;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            _logger.LogWarning(ex, "Could not persist the diagnostics session log.");
            return false;
        }
    }

    /// <summary>
    /// The lines from the last saved session, or empty when there is no file — a first run, or a
    /// clean previous shutdown.
    /// </summary>
    public IReadOnlyList<string> Load()
    {
        try
        {
            lock (_lock)
            {
                return File.Exists(_filePath) ? File.ReadAllLines(_filePath) : [];
            }
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            _logger.LogWarning(ex, "Could not read the diagnostics session log.");
            return [];
        }
    }

    /// <summary>
    /// Deletes the file. Called once its contents have been sent, so the same session is not attached
    /// to every subsequent report the user files.
    /// </summary>
    public void Clear()
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            _logger.LogWarning(ex, "Could not clear the diagnostics session log.");
        }
    }

    private static bool IsFileAccessException(Exception ex)
        => ex is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
}
