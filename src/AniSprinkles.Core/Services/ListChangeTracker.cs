namespace AniSprinkles.Services;

/// <summary>
/// Counts the list-entry saves and removals that may have reached AniList, so a page holding a copy
/// of the list can tell its copy has gone stale (#225). The Library skips reloading for a few minutes
/// after a load; without this, a status changed on the details page, Discover, Search or Up Next left
/// the title in its old section until that window ran out, and an edit made from the stale card saved
/// the old status back.
/// <para>
/// <see cref="CachingAniListClient"/> drives it, not the pages that save: every save goes through
/// that decorator, so a new save path cannot forget to. Readers compare <see cref="Version"/> with
/// the value they last loaded under rather than clearing a flag, so the Anime and Manga lists each
/// notice the same change.
/// </para>
/// <para>
/// It also knows which saves are still on their way (<see cref="WaitForIdleAsync"/>): a details-page
/// edit sent as the user presses Back lands just after the Library has appeared and checked, and
/// waiting for it is what lets that return show the change rather than the old values.
/// </para>
/// </summary>
public sealed class ListChangeTracker
{
    private readonly object _gate = new();
    private long _version;
    private int _inFlight;
    private TaskCompletionSource? _idle;

    /// <summary>Goes up by one for every list change that may have reached AniList.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Records one list change and returns the new <see cref="Version"/>.</summary>
    public long NotifyChanged() => Interlocked.Increment(ref _version);

    /// <summary>A save or removal has been sent. Pair with <see cref="EndChange"/>.</summary>
    public void BeginChange()
    {
        lock (_gate)
        {
            _inFlight++;
        }
    }

    /// <summary>
    /// A save or removal sent with <see cref="BeginChange"/> has finished. <paramref name="changed"/>
    /// is false only when AniList said nothing changed; the version moves before the in-flight count
    /// drops, so a waiter woken by it already sees the new version.
    /// </summary>
    public void EndChange(bool changed)
    {
        if (changed)
        {
            NotifyChanged();
        }

        TaskCompletionSource? idle = null;
        lock (_gate)
        {
            if (_inFlight > 0 && --_inFlight == 0)
            {
                idle = _idle;
                _idle = null;
            }
        }

        idle?.TrySetResult();
    }

    /// <summary>
    /// Completes when no save or removal is in flight, or once <paramref name="timeout"/> has passed,
    /// whichever is first. Never throws on the timeout: a stalled save is the caller's cue to carry on.
    /// </summary>
    public async Task WaitForIdleAsync(TimeSpan timeout, TimeProvider timeProvider)
    {
        Task idle;
        lock (_gate)
        {
            if (_inFlight == 0)
            {
                return;
            }

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idle = _idle.Task;
        }

        try
        {
            await idle.WaitAsync(timeout, timeProvider);
        }
        catch (TimeoutException)
        {
            // Documented above: the caller carries on as if nothing were in flight.
        }
    }
}
