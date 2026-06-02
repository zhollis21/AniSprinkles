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
    public async Task GetStaffAsync_SeedsFirstPageCache_SoSortToggleBackIsServedWithoutFetch()
    {
        var inner = Inner();
        var staff = new Staff { Id = 5 };
        staff.Characters.Add(new StaffCharacterEdge());
        staff.Characters.Add(new StaffCharacterEdge());
        staff.CharactersPageInfo = new PageInfo { CurrentPage = 1, HasNextPage = true };
        inner.GetStaffAsync(5, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(staff);
        var cache = new CachingAniListClient(inner);

        await cache.GetStaffAsync(5);
        // Default voice-roles sort + page 1 + PageSize 25 — the exact key the section requests on toggle-back.
        var (items, pageInfo) = await cache.LoadStaffCharactersPageAsync(5, 1, "FAVOURITES_DESC", 25);

        Assert.Equal(2, items.Count);
        Assert.True(pageInfo!.HasNextPage);
        await inner.DidNotReceive().LoadStaffCharactersPageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStaffAsync_SeededPage_DoesNotCoverADifferentSort()
    {
        var inner = Inner();
        inner.GetStaffAsync(5, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new Staff { Id = 5 });
        inner.LoadStaffCharactersPageAsync(5, 1, "ROLE", 25, Arg.Any<CancellationToken>())
             .Returns(((IReadOnlyList<StaffCharacterEdge>)new List<StaffCharacterEdge>(), (PageInfo?)null));
        var cache = new CachingAniListClient(inner);

        await cache.GetStaffAsync(5);
        await cache.LoadStaffCharactersPageAsync(5, 1, "ROLE", 25);

        // A non-default sort was never seeded, so it must still hit the inner client.
        await inner.Received(1).LoadStaffCharactersPageAsync(5, 1, "ROLE", 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacterAsync_SeedsFirstMediaPageCache_SoSortToggleBackIsServedWithoutFetch()
    {
        var inner = Inner();
        var character = new Character { Id = 9 };
        character.Media.Add(new CharacterMediaEdge());
        character.MediaPageInfo = new PageInfo { CurrentPage = 1, HasNextPage = true };
        inner.GetCharacterAsync(9, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(character);
        var cache = new CachingAniListClient(inner);

        await cache.GetCharacterAsync(9);
        var (items, _) = await cache.LoadCharacterMediaPageAsync(9, 1, "POPULARITY_DESC", 25);

        Assert.Single(items);
        await inner.DidNotReceive().LoadCharacterMediaPageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
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
