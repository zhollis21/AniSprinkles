using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The seams #62 introduced. Small pieces, but every one of them is new code that four or more
/// call sites depend on, and each replaced something that used to be inline.
/// </summary>
public class UserFeedbackExtensionsTests
{
    [Test]
    public async Task FailureSnackbar_DuringAServiceOutage_ShowsTheOutageTitleAndDropsRetry()
    {
        // The outage banner is already on screen and a retry cannot succeed for minutes or hours.
        var feedback = new RecordingUserFeedback();

        await feedback.ShowFailureSnackbarAsync(
            new AniListApiException(ApiErrorKind.ServiceOutage, "down"),
            "Failed to save. Please try again.",
            retryAction: () => { });

        Assert.That(feedback.Snackbars, Has.Exactly(1).Items);
        Assert.That(feedback.Snackbars.Single(), Is.EqualTo("AniList is Down"));
        Assert.That<Action?>(feedback.LastSnackbarAction, Is.Null);
    }

    [TestCase(ApiErrorKind.Network)]
    [TestCase(ApiErrorKind.RateLimited)]
    [TestCase(ApiErrorKind.NotFound)]
    [TestCase(ApiErrorKind.Unknown)]
    public async Task FailureSnackbar_ForEveryOtherKind_KeepsTheFallbackMessageAndTheRetry(ApiErrorKind kind)
    {
        var feedback = new RecordingUserFeedback();
        var retried = false;

        await feedback.ShowFailureSnackbarAsync(
            new AniListApiException(kind, "boom"),
            "Failed to save. Please try again.",
            retryAction: () => retried = true);

        Assert.That(feedback.Snackbars, Has.Exactly(1).Items);
        Assert.That(feedback.Snackbars.Single(), Is.EqualTo("Failed to save. Please try again."));
        Assert.That<Action?>(feedback.LastSnackbarAction, Is.Not.Null);

        feedback.LastSnackbarAction!();
        Assert.That(retried, Is.True);
    }

    [Test]
    public async Task FailureSnackbar_ForANonApiException_StillUsesTheFallback()
    {
        var feedback = new RecordingUserFeedback();

        await feedback.ShowFailureSnackbarAsync(new InvalidOperationException("boom"), "Failed.");

        Assert.That(feedback.Snackbars, Has.Exactly(1).Items);
        Assert.That(feedback.Snackbars.Single(), Is.EqualTo("Failed."));
        Assert.That<Action?>(feedback.LastSnackbarAction, Is.Null);
    }
}

public class MoveToListChoiceTests
{
    [Test]
    public void To_CarriesTheStatusAndIsNotARemoval()
    {
        var choice = MoveToListChoice.To(MediaListStatus.Paused);

        Assert.That(choice.Status, Is.EqualTo(MediaListStatus.Paused));
        Assert.That(choice.IsRemove, Is.False);
    }

    [Test]
    public void Remove_CarriesNoStatus()
    {
        // EntryActionCoordinator branches on IsRemove first and only then reads Status. A Remove
        // that also carried a status would make that ordering load-bearing by accident.
        Assert.That(MoveToListChoice.Remove.IsRemove, Is.True);
        Assert.That(MoveToListChoice.Remove.Status, Is.Null);
    }
}

public class LongPressTapSuppressorTests
{
    // The window comparison is exercised through IsWithinWindow rather than by stamping: the stamp
    // is process-wide and lives for 800ms, so setting it here suppressed navigate commands in any
    // test class that happened to run alongside this one — DetailsSpineTests and
    // MediaBrowsePageModelTests both drive those commands, and neither is serialised against this.

    [Test]
    public void ATapAtTheInstantOfTheLongPress_IsSuppressed()
    {
        // MAUI's TapGestureRecognizer fires on finger-up after a long press, so the card's navigate
        // command would otherwise run underneath the action sheet the long press just opened.
        Assert.That(LongPressTapSuppressor.IsWithinWindow(10_000, 10_000), Is.True);
    }

    [Test]
    public void ATapJustInsideTheWindow_IsSuppressed()
        => Assert.That(LongPressTapSuppressor.IsWithinWindow(10_799, 10_000), Is.True);

    [Test]
    public void ATapOnceTheWindowHasElapsed_IsLetThrough()
        // The other half of the contract: suppression has to end, or a real tap on the same card
        // never navigates.
        => Assert.That(LongPressTapSuppressor.IsWithinWindow(10_800, 10_000), Is.False);

    [Test]
    public void WithNoLongPressEverRecorded_NothingIsSuppressed()
    {
        LongPressTapSuppressor.Reset();

        Assert.That(LongPressTapSuppressor.ShouldSuppressTap(), Is.False);
    }

    [Test]
    public void Stamp_RecordsThePressSoTheFollowUpTapIsSuppressed()
    {
        // The one test that writes the shared static, so it clears it again immediately.
        try
        {
            LongPressTapSuppressor.Stamp();

            Assert.That(LongPressTapSuppressor.ShouldSuppressTap(), Is.True);
        }
        finally
        {
            LongPressTapSuppressor.Reset();
        }
    }

    [Test]
    public void SuppressionWindow_IsLongEnoughToCoverASlowRelease()
    {
        // Stamped at detection AND again at finger-up; the window only has to outlive the gap
        // between the second stamp and the synthetic tap.
        Assert.That(LongPressTapSuppressor.SuppressionWindow >= TimeSpan.FromMilliseconds(500), Is.True);
    }
}

public class OutageStateServiceTests
{
    [Test]
    public void ReportFailure_WithAnOutage_PublishesTheBannerCopy()
    {
        var service = new OutageStateService(new ImmediateDispatcher());

        service.ReportFailure(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        Assert.That(service.IsOutage, Is.True);
        Assert.That(service.Title, Is.EqualTo("AniList is Down"));
        Assert.That(service.Subtitle, Is.Not.Empty);
        Assert.That(service.IconGlyph, Is.Not.Empty);
    }

    [TestCase(ApiErrorKind.Network)]
    [TestCase(ApiErrorKind.RateLimited)]
    [TestCase(ApiErrorKind.NotFound)]
    public void ReportFailure_WithAnyOtherKind_DoesNotRaiseTheBanner(ApiErrorKind kind)
    {
        var service = new OutageStateService(new ImmediateDispatcher());

        service.ReportFailure(new AniListApiException(kind, "boom"));

        Assert.That(service.IsOutage, Is.False);
    }

    [Test]
    public void ReportSuccess_ClearsAPreviouslyRaisedOutage()
    {
        // The state is sticky so the banner does not flap during a partial outage; a success is the
        // only thing that clears it.
        var service = new OutageStateService(new ImmediateDispatcher());
        service.ReportFailure(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        service.ReportSuccess();

        Assert.That(service.IsOutage, Is.False);
        Assert.That(service.Title, Is.Empty);
    }

    [Test]
    public void StateWrites_GoThroughTheDispatcherWhenOffTheUiThread()
    {
        // Callers arrive on pool threads (AniListClient.SendAsync uses ConfigureAwait(false)) and
        // these are bound properties, so the write has to be marshalled.
        var dispatcher = new RecordingDispatcher { IsDispatchRequired = true };
        var service = new OutageStateService(dispatcher);

        service.ReportFailure(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        Assert.That(dispatcher.DispatchCount, Is.EqualTo(1));
        Assert.That(service.IsOutage, Is.True);
    }

    [Test]
    public void StateWrites_RunInlineWhenTheDispatcherCannotQueueThem()
    {
        // Dispatch returns false before there is a dispatcher loop — early startup. Dropping the
        // write there would strand the banner in whatever state it was already in. (The previous
        // implementation called MainThread.IsMainThread, which throws
        // NotImplementedInReferenceAssemblyException off-device and was not covered by its catch.)
        var dispatcher = new RecordingDispatcher { IsDispatchRequired = true, DispatchSucceeds = false };
        var service = new OutageStateService(dispatcher);

        service.ReportFailure(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        Assert.That(dispatcher.DispatchCount, Is.EqualTo(1));
        Assert.That(service.IsOutage, Is.True);
    }

    private sealed class RecordingDispatcher : IDispatcher
    {
        public bool IsDispatchRequired { get; set; }

        public bool DispatchSucceeds { get; set; } = true;

        public int DispatchCount { get; private set; }

        public bool Dispatch(Action action)
        {
            DispatchCount++;
            if (!DispatchSucceeds)
            {
                return false;
            }

            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);

        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }
}
