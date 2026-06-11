using AniSprinkles.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

public class ListOperationRunnerTests
{
    private static ListOperationRunner Runner(IUserFeedback feedback)
        => new(NullLogger.Instance, feedback);

    [Fact]
    public async Task RunAsync_Success_RunsOnCompleteAndShowsNoSnackbar()
    {
        var feedback = Substitute.For<IUserFeedback>();
        var onCompleteCalled = false;

        await Runner(feedback).RunAsync(
            "op", "studio", 18,
            operation: () => Task.CompletedTask,
            loadedCount: () => 25,
            onComplete: () => onCompleteCalled = true);

        Assert.True(onCompleteCalled);
        await feedback.DidNotReceiveWithAnyArgs().ShowSnackbarAsync(default!);
    }

    [Fact]
    public async Task RunAsync_GenericFailure_ShowsGenericSnackbar()
    {
        var feedback = Substitute.For<IUserFeedback>();

        await Runner(feedback).RunAsync(
            "op", "studio", 18,
            operation: () => throw new InvalidOperationException("boom"),
            loadedCount: () => 0);

        await feedback.Received(1)
            .ShowSnackbarAsync("Couldn't update the list. Check your connection and try again.");
    }

    [Fact]
    public async Task RunAsync_AniListApiFailure_ShowsActionableSubtitle()
    {
        var feedback = Substitute.For<IUserFeedback>();
        var apiEx = new AniListApiException(ApiErrorKind.Network, "down");

        await Runner(feedback).RunAsync(
            "op", "staff", 7,
            operation: () => throw apiEx,
            loadedCount: () => 0);

        await feedback.Received(1).ShowSnackbarAsync(apiEx.UserSubtitle);
    }

    [Fact]
    public async Task RunAsync_Failure_RunsOnCompleteBeforeSnackbar()
    {
        var events = new List<string>();
        var feedback = Substitute.For<IUserFeedback>();
        feedback.ShowSnackbarAsync(Arg.Any<string>())
            .Returns(_ => { events.Add("snackbar"); return Task.CompletedTask; });

        await Runner(feedback).RunAsync(
            "op", "character", 3,
            operation: () => throw new InvalidOperationException(),
            loadedCount: () => 0,
            onComplete: () => events.Add("onComplete"));

        // onComplete must re-sync UI (e.g. revert the sort highlight) before the error surfaces.
        Assert.Equal(new[] { "onComplete", "snackbar" }, events);
    }

    [Fact]
    public async Task RunAsync_OperationCanceled_IsTreatedAsFailure()
    {
        // Documents current behavior: cancellation is not special-cased here. In practice
        // PaginatedSection swallows OperationCanceledException internally, so a cancelled Load
        // More/sort returns normally and never reaches this path.
        var feedback = Substitute.For<IUserFeedback>();

        await Runner(feedback).RunAsync(
            "op", "media", 1,
            operation: () => throw new OperationCanceledException(),
            loadedCount: () => 0);

        await feedback.ReceivedWithAnyArgs(1).ShowSnackbarAsync(default!);
    }
}
