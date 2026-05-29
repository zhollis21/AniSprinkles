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
