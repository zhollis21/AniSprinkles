using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The seams #62 introduced. Small pieces, but every one of them is new code that four or more
/// call sites depend on, and each replaced something that used to be inline.
/// </summary>
public class UserFeedbackExtensionsTests
{
    [Fact]
    public async Task FailureSnackbar_DuringAServiceOutage_ShowsTheOutageTitleAndDropsRetry()
    {
        // The outage banner is already on screen and a retry cannot succeed for minutes or hours.
        var feedback = new RecordingUserFeedback();

        await feedback.ShowFailureSnackbarAsync(
            new AniListApiException(ApiErrorKind.ServiceOutage, "down"),
            "Failed to save. Please try again.",
            retryAction: () => { });

        Assert.Equal("AniList is Down", Assert.Single(feedback.Snackbars));
        Assert.Null(feedback.LastSnackbarAction);
    }

    [Theory]
    [InlineData(ApiErrorKind.Network)]
    [InlineData(ApiErrorKind.RateLimited)]
    [InlineData(ApiErrorKind.NotFound)]
    [InlineData(ApiErrorKind.Unknown)]
    public async Task FailureSnackbar_ForEveryOtherKind_KeepsTheFallbackMessageAndTheRetry(ApiErrorKind kind)
    {
        var feedback = new RecordingUserFeedback();
        var retried = false;

        await feedback.ShowFailureSnackbarAsync(
            new AniListApiException(kind, "boom"),
            "Failed to save. Please try again.",
            retryAction: () => retried = true);

        Assert.Equal("Failed to save. Please try again.", Assert.Single(feedback.Snackbars));
        Assert.NotNull(feedback.LastSnackbarAction);

        feedback.LastSnackbarAction!();
        Assert.True(retried);
    }

    [Fact]
    public async Task FailureSnackbar_ForANonApiException_StillUsesTheFallback()
    {
        var feedback = new RecordingUserFeedback();

        await feedback.ShowFailureSnackbarAsync(new InvalidOperationException("boom"), "Failed.");

        Assert.Equal("Failed.", Assert.Single(feedback.Snackbars));
        Assert.Null(feedback.LastSnackbarAction);
    }
}

public class MoveToListChoiceTests
{
    [Fact]
    public void To_CarriesTheStatusAndIsNotARemoval()
    {
        var choice = MoveToListChoice.To(MediaListStatus.Paused);

        Assert.Equal(MediaListStatus.Paused, choice.Status);
        Assert.False(choice.IsRemove);
    }

    [Fact]
    public void Remove_CarriesNoStatus()
    {
        // EntryActionCoordinator branches on IsRemove first and only then reads Status. A Remove
        // that also carried a status would make that ordering load-bearing by accident.
        Assert.True(MoveToListChoice.Remove.IsRemove);
        Assert.Null(MoveToListChoice.Remove.Status);
    }
}

public class LongPressTapSuppressorTests
{
    [Fact]
    public void ShouldSuppressTap_ImmediatelyAfterALongPress_IsTrue()
    {
        // MAUI's TapGestureRecognizer fires on finger-up after a long press, so the card's navigate
        // command would otherwise run underneath the action sheet the long press just opened.
        LongPressTapSuppressor.Stamp();

        Assert.True(LongPressTapSuppressor.ShouldSuppressTap());
    }

    [Fact]
    public void SuppressionWindow_IsLongEnoughToCoverASlowRelease()
    {
        // Stamped at detection AND again at finger-up; the window only has to outlive the gap
        // between the second stamp and the synthetic tap.
        Assert.True(LongPressTapSuppressor.SuppressionWindow >= TimeSpan.FromMilliseconds(500));
    }
}

public class OutageStateServiceTests
{
    [Fact]
    public void ReportFailure_WithAnOutage_PublishesTheBannerCopy()
    {
        var service = new OutageStateService(new ImmediateDispatcher());

        service.ReportFailure(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        Assert.True(service.IsOutage);
        Assert.Equal("AniList is Down", service.Title);
        Assert.NotEmpty(service.Subtitle);
        Assert.NotEmpty(service.IconGlyph);
    }

    [Theory]
    [InlineData(ApiErrorKind.Network)]
    [InlineData(ApiErrorKind.RateLimited)]
    [InlineData(ApiErrorKind.NotFound)]
    public void ReportFailure_WithAnyOtherKind_DoesNotRaiseTheBanner(ApiErrorKind kind)
    {
        var service = new OutageStateService(new ImmediateDispatcher());

        service.ReportFailure(new AniListApiException(kind, "boom"));

        Assert.False(service.IsOutage);
    }

    [Fact]
    public void ReportSuccess_ClearsAPreviouslyRaisedOutage()
    {
        // The state is sticky so the banner does not flap during a partial outage; a success is the
        // only thing that clears it.
        var service = new OutageStateService(new ImmediateDispatcher());
        service.ReportFailure(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        service.ReportSuccess();

        Assert.False(service.IsOutage);
        Assert.Empty(service.Title);
    }

    [Fact]
    public void StateWrites_GoThroughTheDispatcherWhenOffTheUiThread()
    {
        // Callers arrive on pool threads (AniListClient.SendAsync uses ConfigureAwait(false)) and
        // these are bound properties, so the write has to be marshalled.
        var dispatcher = new RecordingDispatcher { IsDispatchRequired = true };
        var service = new OutageStateService(dispatcher);

        service.ReportFailure(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        Assert.Equal(1, dispatcher.DispatchCount);
        Assert.True(service.IsOutage);
    }

    [Fact]
    public void StateWrites_RunInlineWhenTheDispatcherCannotQueueThem()
    {
        // Dispatch returns false before there is a dispatcher loop — early startup. Dropping the
        // write there would strand the banner in whatever state it was already in. (The previous
        // implementation called MainThread.IsMainThread, which throws
        // NotImplementedInReferenceAssemblyException off-device and was not covered by its catch.)
        var dispatcher = new RecordingDispatcher { IsDispatchRequired = true, DispatchSucceeds = false };
        var service = new OutageStateService(dispatcher);

        service.ReportFailure(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        Assert.Equal(1, dispatcher.DispatchCount);
        Assert.True(service.IsOutage);
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
