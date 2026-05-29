using AniSprinkles.Models;
using NSubstitute;

namespace AniSprinkles.UnitTests;

// NSubstitute setup/verification requires Arg.Any<CancellationToken>() matchers, which inherently
// conflict with xUnit1051's "pass TestContext.Current.CancellationToken" recommendation. Suppress
// it for this file; these tests are deterministic and don't depend on cancellation.
#pragma warning disable xUnit1051

public class CachingAniListClientTests
{
    private static IAniListClient Inner() => Substitute.For<IAniListClient>();

    [Fact]
    public async Task GetCharacterAsync_CalledTwiceForSameKey_FetchesInnerOnce()
    {
        var inner = Inner();
        inner.GetCharacterAsync(1, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new Character { Id = 1 });
        var cache = new CachingAniListClient(inner);

        var first = await cache.GetCharacterAsync(1);
        var second = await cache.GetCharacterAsync(1);

        Assert.Same(first, second);
        await inner.Received(1).GetCharacterAsync(1, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacterAsync_DifferentArguments_FetchEachSeparately()
    {
        var inner = Inner();
        inner.GetCharacterAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(ci => new Character { Id = ci.ArgAt<int>(0) });
        var cache = new CachingAniListClient(inner);

        var a = await cache.GetCharacterAsync(1);
        var b = await cache.GetCharacterAsync(2);
        var aSort = await cache.GetCharacterAsync(1, mediaSort: "SCORE_DESC");

        Assert.Equal(1, a!.Id);
        Assert.Equal(2, b!.Id);
        // id 1 default-sort, id 2 default-sort, and id 1 different-sort are three distinct keys.
        await inner.Received(3).GetCharacterAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacterAsync_ConcurrentSameKey_CoalescesToOneFetch()
    {
        var inner = Inner();
        var gate = new TaskCompletionSource<Character?>(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.GetCharacterAsync(7, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(gate.Task);
        var cache = new CachingAniListClient(inner);

        var first = cache.GetCharacterAsync(7);
        var second = cache.GetCharacterAsync(7);

        gate.SetResult(new Character { Id = 7 });
        var results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.Equal(7, r!.Id));
        await inner.Received(1).GetCharacterAsync(7, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStaffAsync_FailedFetch_IsNotCached()
    {
        var inner = Inner();
        inner.GetStaffAsync(5, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(
                 _ => Task.FromException<Staff?>(new AniListApiException(ApiErrorKind.Network, "boom")),
                 _ => Task.FromResult<Staff?>(new Staff { Id = 5 }));
        var cache = new CachingAniListClient(inner);

        await Assert.ThrowsAsync<AniListApiException>(() => cache.GetStaffAsync(5));
        var recovered = await cache.GetStaffAsync(5);

        Assert.Equal(5, recovered!.Id);
        await inner.Received(2).GetStaffAsync(5, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveMediaListEntryAsync_CalledTwice_PassesThroughEachTime()
    {
        var inner = Inner();
        var entry = new MediaListEntry { Id = 1 };
        inner.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>()).Returns(entry);
        var cache = new CachingAniListClient(inner);

        await cache.SaveMediaListEntryAsync(entry);
        await cache.SaveMediaListEntryAsync(entry);

        await inner.Received(2).SaveMediaListEntryAsync(entry, Arg.Any<CancellationToken>());
    }
}
