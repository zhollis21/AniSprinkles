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

    [Test]
    public async Task ToggleAsync_Success_FlipsHeartBumpsCountAndCallsApi()
    {
        var (client, feedback) = Deps();
        var entity = new Media { Id = 42, IsFavourite = false, Favourites = 100 };

        var result = await Runner(client, feedback).ToggleAsync(entity, FavouriteKind.Anime, () => { }, () => { });

        Assert.That(result, Is.True);
        Assert.That(entity.IsFavourite, Is.True);
        Assert.That(entity.Favourites, Is.EqualTo(101));
        await client.Received(1).ToggleFavouriteAsync(FavouriteKind.Anime, 42, Arg.Any<CancellationToken>());
        await feedback.DidNotReceiveWithAnyArgs().ShowSnackbarAsync(default!, default!, default!);
    }

    [Test]
    public async Task ToggleAsync_Unfavorite_DecrementsCount()
    {
        var (client, feedback) = Deps();
        var entity = new Character { Id = 7, IsFavourite = true, Favourites = 50 };

        await Runner(client, feedback).ToggleAsync(entity, FavouriteKind.Character, () => { }, () => { });

        Assert.That(entity.IsFavourite, Is.False);
        Assert.That(entity.Favourites, Is.EqualTo(49));
    }

    [Test]
    public async Task ToggleAsync_FavouriteFromNullCount_BecomesOne()
    {
        var (client, feedback) = Deps();
        var entity = new Staff { Id = 3, IsFavourite = false, Favourites = null };

        await Runner(client, feedback).ToggleAsync(entity, FavouriteKind.Staff, () => { }, () => { });

        Assert.That(entity.IsFavourite, Is.True);
        Assert.That(entity.Favourites, Is.EqualTo(1));
    }

    [Test]
    public async Task ToggleAsync_Failure_RevertsHeartAndCountAndShowsRetrySnackbar()
    {
        var (client, feedback) = Deps();
        client.ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new AniListApiException(ApiErrorKind.Network, "down")));
        var entity = new Studio { Id = 9, IsFavourite = false, Favourites = 200 };

        var result = await Runner(client, feedback).ToggleAsync(entity, FavouriteKind.Studio, () => { }, () => { });

        Assert.That(result, Is.False);
        // Heart and count rolled back together.
        Assert.That(entity.IsFavourite, Is.False);
        Assert.That(entity.Favourites, Is.EqualTo(200));
        await feedback.Received(1).ShowSnackbarAsync(
            "Failed to update favorite. Please try again.", "Retry", Arg.Any<Action>());
    }

    [Test]
    public async Task ToggleAsync_WhileInFlight_SecondCallIsNoOp()
    {
        var (client, feedback) = Deps();
        var gate = new TaskCompletionSource<bool>();
        client.ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);
        var entity = new Media { Id = 1, IsFavourite = false, Favourites = 10 };
        var runner = Runner(client, feedback);

        var first = runner.ToggleAsync(entity, FavouriteKind.Anime, () => { }, () => { });
        Assert.That(runner.IsBusy, Is.True);

        // Rapid second tap while the first is still in flight: skipped, no second mutation.
        var second = await runner.ToggleAsync(entity, FavouriteKind.Anime, () => { }, () => { });
        Assert.That(second, Is.False);

        gate.SetResult(true);
        Assert.That(await first, Is.True);

        await client.Received(1).ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.That(runner.IsBusy, Is.False);
    }

    [Test]
    public async Task ToggleAsync_InvokesOnChangedAndClearsBusyWhenDone()
    {
        var (client, feedback) = Deps();
        var entity = new Media { Id = 5, IsFavourite = false, Favourites = 0 };
        var runner = Runner(client, feedback);
        var changes = 0;

        await runner.ToggleAsync(entity, FavouriteKind.Anime, () => changes++, () => { });

        // Busy-on, optimistic-apply, and busy-off each notify.
        Assert.That(changes >= 3, Is.True);
        Assert.That(runner.IsBusy, Is.False);
    }
}
