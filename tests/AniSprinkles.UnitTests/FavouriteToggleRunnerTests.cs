using AniSprinkles.Models;
using AniSprinkles.PageModels;
using AniSprinkles.Services;
using AniSprinkles.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

public class FavouriteToggleRunnerTests
{
    private static FavouriteToggleRunner Runner(IAniListClient client, IUserFeedback feedback)
        => new(client, feedback, NullLogger.Instance);

    private static (IAniListClient Client, IUserFeedback Feedback) Deps()
    {
        var client = Substitute.For<IAniListClient>();
        client.ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        return (client, Substitute.For<IUserFeedback>());
    }

    [Fact]
    public async Task ToggleAsync_Success_FlipsHeartBumpsCountAndCallsApi()
    {
        var (client, feedback) = Deps();
        var entity = new Media { Id = 42, IsFavourite = false, Favourites = 100 };

        var result = await Runner(client, feedback).ToggleAsync(entity, FavouriteKind.Anime, () => { }, () => { });

        Assert.True(result);
        Assert.True(entity.IsFavourite);
        Assert.Equal(101, entity.Favourites);
        await client.Received(1).ToggleFavouriteAsync(FavouriteKind.Anime, 42, Arg.Any<CancellationToken>());
        await feedback.DidNotReceiveWithAnyArgs().ShowSnackbarAsync(default!, default!, default!);
    }

    [Fact]
    public async Task ToggleAsync_Unfavorite_DecrementsCount()
    {
        var (client, feedback) = Deps();
        var entity = new Character { Id = 7, IsFavourite = true, Favourites = 50 };

        await Runner(client, feedback).ToggleAsync(entity, FavouriteKind.Character, () => { }, () => { });

        Assert.False(entity.IsFavourite);
        Assert.Equal(49, entity.Favourites);
    }

    [Fact]
    public async Task ToggleAsync_FavouriteFromNullCount_BecomesOne()
    {
        var (client, feedback) = Deps();
        var entity = new Staff { Id = 3, IsFavourite = false, Favourites = null };

        await Runner(client, feedback).ToggleAsync(entity, FavouriteKind.Staff, () => { }, () => { });

        Assert.True(entity.IsFavourite);
        Assert.Equal(1, entity.Favourites);
    }

    [Fact]
    public async Task ToggleAsync_Failure_RevertsHeartAndCountAndShowsRetrySnackbar()
    {
        var (client, feedback) = Deps();
        client.ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new AniListApiException(ApiErrorKind.Network, "down")));
        var entity = new Studio { Id = 9, IsFavourite = false, Favourites = 200 };

        var result = await Runner(client, feedback).ToggleAsync(entity, FavouriteKind.Studio, () => { }, () => { });

        Assert.False(result);
        // Heart and count rolled back together.
        Assert.False(entity.IsFavourite);
        Assert.Equal(200, entity.Favourites);
        await feedback.Received(1).ShowSnackbarAsync(
            "Failed to update favorite. Please try again.", "Retry", Arg.Any<Action>());
    }

    [Fact]
    public async Task ToggleAsync_WhileInFlight_SecondCallIsNoOp()
    {
        var (client, feedback) = Deps();
        var gate = new TaskCompletionSource<bool>();
        client.ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);
        var entity = new Media { Id = 1, IsFavourite = false, Favourites = 10 };
        var runner = Runner(client, feedback);

        var first = runner.ToggleAsync(entity, FavouriteKind.Anime, () => { }, () => { });
        Assert.True(runner.IsBusy);

        // Rapid second tap while the first is still in flight: skipped, no second mutation.
        var second = await runner.ToggleAsync(entity, FavouriteKind.Anime, () => { }, () => { });
        Assert.False(second);

        gate.SetResult(true);
        Assert.True(await first);

        await client.Received(1).ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task ToggleAsync_InvokesOnChangedAndClearsBusyWhenDone()
    {
        var (client, feedback) = Deps();
        var entity = new Media { Id = 5, IsFavourite = false, Favourites = 0 };
        var runner = Runner(client, feedback);
        var changes = 0;

        await runner.ToggleAsync(entity, FavouriteKind.Anime, () => changes++, () => { });

        // Busy-on, optimistic-apply, and busy-off each notify.
        Assert.True(changes >= 3);
        Assert.False(runner.IsBusy);
    }
}
