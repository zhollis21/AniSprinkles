using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>The in-flight half of <see cref="ListChangeTracker"/> (#225), on its own.</summary>
public class ListChangeTrackerTests
{
    [Test]
    public void WithNothingInFlight_TheWaitCompletesAtOnce()
    {
        var tracker = new ListChangeTracker();

        Assert.That(tracker.WaitForIdleAsync(TimeSpan.FromSeconds(5), new ManualTimeProvider(DateTimeOffset.UnixEpoch)).IsCompleted, Is.True);
    }

    [Test]
    public async Task TwoSavesInFlight_TheWaitEndsOnlyWhenBothHave()
    {
        var tracker = new ListChangeTracker();
        tracker.BeginChange();
        tracker.BeginChange();

        var wait = tracker.WaitForIdleAsync(TimeSpan.FromSeconds(5), new ManualTimeProvider(DateTimeOffset.UnixEpoch));
        tracker.EndChange(changed: true);
        await Task.Yield();
        Assert.That(wait.IsCompleted, Is.False, "ended with a save still out");

        tracker.EndChange(changed: true);
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(tracker.Version, Is.EqualTo(2));
    }

    [Test]
    public void ANoChangeEnd_LeavesTheVersion_ButStillEndsTheWait()
    {
        var tracker = new ListChangeTracker();
        tracker.BeginChange();
        var wait = tracker.WaitForIdleAsync(TimeSpan.FromSeconds(5), new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        tracker.EndChange(changed: false);

        Assert.That(tracker.Version, Is.Zero);
        Assert.That(() => wait.WaitAsync(TimeSpan.FromSeconds(5)), Throws.Nothing);
    }

    [Test]
    public void AnUnpairedEnd_DoesNotDriveTheCountBelowZero()
    {
        // A later save must still be waited for.
        var tracker = new ListChangeTracker();
        tracker.EndChange(changed: false);
        tracker.BeginChange();

        Assert.That(tracker.WaitForIdleAsync(TimeSpan.FromSeconds(5), new ManualTimeProvider(DateTimeOffset.UnixEpoch)).IsCompleted, Is.False);
        tracker.EndChange(changed: false);
    }

    [Test]
    public async Task AnEditHold_IsWaitedFor_AndMovesNoVersion_AndLetsGoOnlyOnce()
    {
        var tracker = new ListChangeTracker();
        var first = tracker.HoldForEdit();
        var second = tracker.HoldForEdit();
        var wait = tracker.WaitForIdleAsync(TimeSpan.FromSeconds(5), new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        first.Dispose();
        first.Dispose();
        await Task.Yield();
        Assert.That(wait.IsCompleted, Is.False, "a second Dispose let go of the other edit");

        second.Dispose();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(tracker.Version, Is.Zero);
    }
}