namespace AniSprinkles.Services;

/// <summary>
/// Decides when the in-memory diagnostics ring is written to disk and when that file may be thrown
/// away again (#112).
/// <para>
/// Separate from the platform callbacks that drive it — the global exception handlers and the
/// activity's pause and resume — because the interesting part is not the callback, it is the
/// ownership rule below, and that rule is wrong in a way nothing else would notice.
/// </para>
/// </summary>
public sealed class DiagnosticsSessionFlusher
{
    private readonly DiagnosticsLogBuffer _buffer;
    private readonly DiagnosticsSessionLog _sessionLog;
    private readonly object _lock = new();
    private bool _flushedByThisProcess;

    public DiagnosticsSessionFlusher(DiagnosticsLogBuffer buffer, DiagnosticsSessionLog sessionLog)
    {
        _buffer = buffer;
        _sessionLog = sessionLog;
    }

    /// <summary>
    /// Writes the current ring out. Returns whether anything was persisted — false when the ring was
    /// empty, or when the write failed.
    /// </summary>
    public bool Flush()
    {
        lock (_lock)
        {
            if (!_sessionLog.Save(_buffer.Snapshot()))
            {
                return false;
            }

            _flushedByThisProcess = true;
            return true;
        }
    }

    /// <summary>
    /// Drops the file, but only if <i>this</i> process is the one that wrote it. Called on resume.
    /// <para>
    /// The ownership test is the whole point. The file exists to outlive a process that died holding
    /// the ring — so if we are still here to resume, it was not needed, and keeping it would put
    /// every one of those lines into the next report twice, because the live ring still has them.
    /// But a file left by a <i>previous</i> process is exactly the thing worth sending, and clearing
    /// it on this process's first resume would delete the crash before the user could report it.
    /// </para>
    /// </summary>
    public bool ClearIfOwnedByThisProcess()
    {
        lock (_lock)
        {
            if (!_flushedByThisProcess)
            {
                return false;
            }

            _sessionLog.Clear();
            _flushedByThisProcess = false;
            return true;
        }
    }
}
