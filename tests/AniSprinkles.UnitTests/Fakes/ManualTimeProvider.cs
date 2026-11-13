namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// A minimal controllable <see cref="TimeProvider"/> for deterministic time-based tests.
/// Supports <see cref="GetUtcNow"/> and one-shot timers (which is what
/// <c>Task.Delay(TimeSpan, TimeProvider, CancellationToken)</c> uses). Call <see cref="Advance"/>
/// to move the clock forward and fire any due timers synchronously — no real sleeping.
///
/// Self-contained on purpose: the Microsoft.Extensions.TimeProvider.Testing package isn't part of
/// this project's restore graph, and we only need a sliver of its behaviour.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Whether any timer is waiting to fire.</summary>
    public bool HasPendingTimers
    {
        get
        {
            lock (_lock)
            {
                return _timers.Any(t => !t.Disposed);
            }
        }
    }

    /// <summary>
    /// Drives <paramref name="task"/> through its delays on this clock, a <paramref name="step"/>
    /// at a time. The clock moves only while a timer is pending, so time never runs ahead of a
    /// delay the code has not registered yet. The limit is real time, not a number of steps.
    /// A fixed count of advance-and-yield steps hung CI (#178): on a loaded runner the steps ran
    /// out before the code under test registered its next delay, that delay never fired, and the
    /// test's await on the task waited forever. Here slow code is waited for, and code that really
    /// stalls fails the test instead of hanging the run.
    /// </summary>
    public async Task AdvanceUntilCompleteAsync(Task task, TimeSpan step)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"The task did not complete within 10 seconds of real time (fake clock at {GetUtcNow():O}).");
            }

            if (HasPendingTimers)
            {
                Advance(step);
            }

            await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(5)));
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_lock)
        {
            _now += by;
        }

        FakeTimer[] due;
        lock (_lock)
        {
            due = _timers.Where(t => !t.Disposed && t.DueAt <= _now).ToArray();
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state, _now + dueTime, period);
        lock (_lock)
        {
            _timers.Add(timer);
        }

        if (dueTime <= TimeSpan.Zero)
        {
            timer.Fire();
        }

        return timer;
    }

    private void Remove(FakeTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period;

        public FakeTimer(ManualTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset dueAt, TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            DueAt = dueAt;
            _period = period;
        }

        public DateTimeOffset DueAt { get; private set; }

        public bool Disposed { get; private set; }

        public void Fire()
        {
            if (Disposed)
            {
                return;
            }

            _callback(_state);

            if (_period <= TimeSpan.Zero || _period == Timeout.InfiniteTimeSpan)
            {
                Disposed = true; // one-shot (Task.Delay)
            }
            else
            {
                DueAt = _owner.GetUtcNow() + _period;
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = _owner.GetUtcNow() + dueTime;
            _period = period;
            return true;
        }

        public void Dispose()
        {
            Disposed = true;
            _owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
