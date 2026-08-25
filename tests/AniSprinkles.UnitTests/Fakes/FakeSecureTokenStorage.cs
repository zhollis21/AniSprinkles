using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// A dictionary-backed <see cref="ISecureTokenStorage"/> that can count reads, park a chosen read
/// open, and fail a chosen read — the three things #119's acceptance criteria need to observe.
/// <para>
/// Reads are addressed by <em>key and occurrence</em> ("the second read of the token key") rather
/// than by a global sequence number. That distinction is load-bearing: the whole point of the fix is
/// that the second caller stops reading, so a global number that means "the other caller's token
/// read" before the fix means "the first caller's expiry read" after it, and the test would gate the
/// wrong operation. Addressing a read that never happens is simply inert.
/// </para>
/// </summary>
public sealed class FakeSecureTokenStorage : ISecureTokenStorage
{
    private readonly Dictionary<string, string> _values = [];
    private readonly Lock _sync = new();
    private readonly List<string> _reads = [];
    private readonly Dictionary<(string Key, int Occurrence), TaskCompletionSource> _holds = [];
    private readonly Dictionary<(string Key, int Occurrence), TaskCompletionSource> _entered = [];
    private readonly HashSet<(string Key, int Occurrence)> _failures = [];

    /// <summary>Every key read, in order, including reads that went on to throw.</summary>
    public IReadOnlyList<string> Reads
    {
        get
        {
            lock (_sync)
            {
                return [.. _reads];
            }
        }
    }

    public int ReadCountFor(string key) => Reads.Count(k => k == key);

    /// <summary>
    /// Seeds a stored value. Locked like every other <c>_values</c> access even though seeding
    /// happens during setup: sign-in and sign-out publish through <see cref="SetAsync"/> and
    /// <see cref="Remove"/> while a reader is parked mid-<see cref="GetAsync"/>, so the dictionary is
    /// reachable from more than one thread and one unguarded path would be enough to make a
    /// concurrency test nondeterministic.
    /// </summary>
    public void Seed(string key, string value)
    {
        lock (_sync)
        {
            _values[key] = value;
        }
    }

    /// <summary>Parks the <paramref name="occurrence"/>-th read of <paramref name="key"/> until released.</summary>
    public void HoldRead(string key, int occurrence = 1)
    {
        lock (_sync)
        {
            _holds[(key, occurrence)] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _entered[(key, occurrence)] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Completes once a caller is parked inside that read.</summary>
    public Task ReadEntered(string key, int occurrence = 1)
    {
        lock (_sync)
        {
            return _entered.TryGetValue((key, occurrence), out var tcs) ? tcs.Task : Task.CompletedTask;
        }
    }

    public void ReleaseRead(string key, int occurrence = 1)
    {
        lock (_sync)
        {
            _holds.TryGetValue((key, occurrence), out var tcs);
            tcs?.TrySetResult();
        }
    }

    /// <summary>Makes that read throw.</summary>
    public void FailRead(string key, int occurrence = 1)
    {
        lock (_sync)
        {
            _failures.Add((key, occurrence));
        }
    }

    /// <summary>
    /// Waits until <paramref name="key"/> has been read at least <paramref name="count"/> times, or
    /// the timeout elapses. The timeout is the expected path once the bug is fixed — the second
    /// caller is parked on the load gate and never reaches storage — so this returns quietly rather
    /// than throwing.
    /// </summary>
    public async Task WaitForReadsAsync(string key, int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (ReadCountFor(key) < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
    }

    public async Task<string?> GetAsync(string key)
    {
        (string, int) address;
        TaskCompletionSource? hold;
        TaskCompletionSource? entered;

        lock (_sync)
        {
            _reads.Add(key);
            address = (key, _reads.Count(k => k == key));
            _holds.TryGetValue(address, out hold);
            _entered.TryGetValue(address, out entered);
        }

        if (hold is not null)
        {
            entered?.TrySetResult();
            await hold.Task;
        }

        bool shouldFail;
        lock (_sync)
        {
            shouldFail = _failures.Contains(address);
        }

        if (shouldFail)
        {
            throw new InvalidOperationException($"secure storage read of '{address.Item1}' #{address.Item2} failed");
        }

        // Taken after the await, never across it, so a parked reader cannot block a writer.
        lock (_sync)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public Task SetAsync(string key, string value)
    {
        lock (_sync)
        {
            _values[key] = value;
        }

        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        lock (_sync)
        {
            _values.Remove(key);
        }
    }
}
