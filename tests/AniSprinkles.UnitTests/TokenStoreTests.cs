using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #119. <c>AuthService</c> is a singleton and its token load had no synchronization, so two callers
/// arriving before the first load completed both entered it. Since #116 the load has a fallback catch
/// treating an unreadable token as "signed out", which is right for one caller and wrong for two: a
/// failing read wipes the token a successful one just published.
/// <para>
/// A short timeout appears in the concurrent tests. It is the *expected* path once the fix is in —
/// the second caller parks on the load gate and never reaches storage, so there is no second read to
/// wait for. Against the unsynchronized version it completes almost immediately.
/// </para>
/// </summary>
public class TokenStoreTests
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static TokenStore Build(FakeSecureTokenStorage storage, DateTimeOffset? now = null)
        => new(storage, new ManualTimeProvider(now ?? Now), new RecordingLogger<TokenStore>());

    private static FakeSecureTokenStorage WithToken(string token = "tok", DateTimeOffset? expiresAt = null)
    {
        var storage = new FakeSecureTokenStorage();
        storage.Seed(TokenStore.TokenKey, token);
        if (expiresAt is not null)
        {
            storage.Seed(TokenStore.TokenExpiryKey, expiresAt.Value.ToString("O"));
        }

        return storage;
    }

    [Fact]
    public async Task TwoConcurrentGets_OnAColdToken_PerformExactlyOneRead()
    {
        var storage = WithToken();
        storage.HoldRead(TokenStore.TokenKey);
        var store = Build(storage);

        var first = Task.Run(() => store.GetAsync());
        await storage.ReadEntered(TokenStore.TokenKey);

        var second = Task.Run(() => store.GetAsync());
        await storage.WaitForReadsAsync(TokenStore.TokenKey, 2, SettleTimeout);

        storage.ReleaseRead(TokenStore.TokenKey);
        await Task.WhenAll(first, second);

        Assert.Equal(1, storage.ReadCountFor(TokenStore.TokenKey));
    }

    [Fact]
    public async Task TwoSequentialGets_WhenNothingIsStored_PerformExactlyOneRead()
    {
        // The signed-out case. Nothing is stored, so the token stays null after the load — a
        // double-check that re-tests the token rather than recording that a load happened is still
        // true for the second caller, and reads again.
        var storage = new FakeSecureTokenStorage();
        var store = Build(storage);

        Assert.Equal(TokenState.Absent, (await store.GetAsync(TestContext.Current.CancellationToken)).State);
        Assert.Equal(TokenState.Absent, (await store.GetAsync(TestContext.Current.CancellationToken)).State);

        Assert.Equal(1, storage.ReadCountFor(TokenStore.TokenKey));
    }

    [Fact]
    public async Task AFailingRead_CannotClearATokenPublishedByASuccessfulOne()
    {
        // The reported bug. Both callers are in flight; the first publishes a valid token, then the
        // second's read throws and its catch wipes what the first published.
        var storage = WithToken();
        storage.HoldRead(TokenStore.TokenKey, 1); // the first caller
        storage.HoldRead(TokenStore.TokenKey, 2); // the second caller, if it reads at all
        storage.FailRead(TokenStore.TokenKey, 2);
        var store = Build(storage);

        var first = Task.Run(() => store.GetAsync());
        await storage.ReadEntered(TokenStore.TokenKey, 1);

        var second = Task.Run(() => store.GetAsync());
        await storage.WaitForReadsAsync(TokenStore.TokenKey, 2, SettleTimeout);

        // Let the successful caller finish and publish before the failing one is released.
        storage.ReleaseRead(TokenStore.TokenKey, 1);
        Assert.Equal(TokenState.Valid, (await first).State);

        storage.ReleaseRead(TokenStore.TokenKey, 2);

        // The reported symptom: the concurrent caller reports a signed-in user as signed out.
        Assert.Equal(TokenState.Valid, (await second).State);

        // And it wiped the shared state on its way out. Fail any further read so a transparent
        // re-load cannot mask the wipe — after the fix there is no further read to fail.
        storage.FailRead(TokenStore.TokenKey, 3);
        Assert.Equal("tok", (await store.GetAsync(TestContext.Current.CancellationToken)).AccessToken);
    }

    [Fact]
    public async Task AConcurrentCaller_NeverSeesATokenWhoseExpiryHasNotLoadedYet()
    {
        // The token is published one line before the expiry is read, so a caller arriving in between
        // sees a non-null token with no expiry — and no expiry reads as "not expired".
        var storage = WithToken(expiresAt: Now.AddHours(-1));
        storage.HoldRead(TokenStore.TokenExpiryKey);
        var store = Build(storage);

        var first = Task.Run(() => store.GetAsync());
        await storage.ReadEntered(TokenStore.TokenExpiryKey);

        // Unsynchronized, the second caller skips the load entirely and returns without reading, so
        // there is no read to wait on — wait for the call itself, or for the gate to hold it.
        var second = Task.Run(() => store.GetAsync());
        await Task.WhenAny(second, Task.Delay(SettleTimeout, TestContext.Current.CancellationToken));

        storage.ReleaseRead(TokenStore.TokenExpiryKey);
        var results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.Equal(TokenState.Expired, r.State));
    }

    [Fact]
    public async Task Cancellation_PropagatesToTheCallerThatOwnsTheToken()
    {
        var storage = WithToken();
        storage.HoldRead(TokenStore.TokenKey);
        var store = Build(storage);

        using var cts = new CancellationTokenSource();

        var first = Task.Run(() => store.GetAsync());
        await storage.ReadEntered(TokenStore.TokenKey);

        var second = Task.Run(() => store.GetAsync(cts.Token));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        // The caller that owns the load is unaffected by someone else's cancellation.
        storage.ReleaseRead(TokenStore.TokenKey);
        Assert.Equal(TokenState.Valid, (await first).State);
    }

    [Fact]
    public async Task AnUnreadableToken_ReportsAbsentRatherThanThrowing()
    {
        var storage = WithToken();
        storage.FailRead(TokenStore.TokenKey);
        var store = Build(storage);

        Assert.Equal(TokenState.Absent, (await store.GetAsync(TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task AnUnreadableToken_IsNotRetriedOnEveryCall()
    {
        // A keystore that is broken stays broken. Re-reading it on every token check would put a
        // failing platform call on every tab's OnAppearing.
        var storage = WithToken();
        storage.FailRead(TokenStore.TokenKey);
        var store = Build(storage);

        await store.GetAsync(TestContext.Current.CancellationToken);
        await store.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, storage.ReadCountFor(TokenStore.TokenKey));
    }

    [Fact]
    public async Task AnExpiredToken_ReportsExpired()
    {
        var store = Build(WithToken(expiresAt: Now.AddMinutes(-1)));

        Assert.Equal(TokenState.Expired, (await store.GetAsync(TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task AFreshToken_ReportsValid()
    {
        var store = Build(WithToken(expiresAt: Now.AddHours(1)));

        var lookup = await store.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TokenState.Valid, lookup.State);
        Assert.Equal("tok", lookup.AccessToken);
    }

    [Fact]
    public async Task ATokenWithNoStoredExpiry_ReportsValid()
    {
        var store = Build(WithToken());

        Assert.Equal(TokenState.Valid, (await store.GetAsync(TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task SetThenGet_ReturnsTheNewTokenWithoutReadingStorage()
    {
        var storage = new FakeSecureTokenStorage();
        var store = Build(storage);

        await store.SetAsync("fresh", Now.AddHours(1));

        Assert.Equal("fresh", (await store.GetAsync(TestContext.Current.CancellationToken)).AccessToken);
        Assert.Empty(storage.Reads);
    }

    [Fact]
    public async Task ClearThenGet_ReportsAbsentWithoutRereadingStorage()
    {
        // Sign-out knows what it just removed. Going back to storage would re-read the keys it
        // deleted a line earlier.
        var storage = new FakeSecureTokenStorage();
        var store = Build(storage);
        await store.SetAsync("fresh", Now.AddHours(1));

        store.Clear();

        Assert.Equal(TokenState.Absent, (await store.GetAsync(TestContext.Current.CancellationToken)).State);
        Assert.Empty(storage.Reads);
    }

    [Fact]
    public async Task SigningInWhileALoadIsInFlight_KeepsTheNewToken()
    {
        // Sign-in publishes without going through the load, so the load's own publish has to yield
        // to it. Otherwise the read that started before the sign-in overwrites the fresh token with
        // whatever storage held back then — here, nothing.
        var storage = new FakeSecureTokenStorage();
        storage.HoldRead(TokenStore.TokenKey);
        var store = Build(storage);

        var inFlight = Task.Run(() => store.GetAsync());
        await storage.ReadEntered(TokenStore.TokenKey);

        await store.SetAsync("fresh", Now.AddHours(1));

        storage.ReleaseRead(TokenStore.TokenKey);
        await inFlight;

        Assert.Equal("fresh", (await store.GetAsync(TestContext.Current.CancellationToken)).AccessToken);
    }

    [Fact]
    public async Task SigningOutWhileALoadIsInFlight_StaysSignedOut()
    {
        var storage = WithToken();
        storage.HoldRead(TokenStore.TokenKey);
        var store = Build(storage);

        var inFlight = Task.Run(() => store.GetAsync());
        await storage.ReadEntered(TokenStore.TokenKey);

        store.Clear();

        storage.ReleaseRead(TokenStore.TokenKey);
        await inFlight;

        Assert.Equal(TokenState.Absent, (await store.GetAsync(TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task SigningInAfterAClear_ServesTheNewToken()
    {
        // The risk of keeping the store "loaded" across a sign-out is serving the previous account's
        // token to the next one. Sign-in republishes, so it cannot happen.
        var storage = new FakeSecureTokenStorage();
        var store = Build(storage);
        await store.SetAsync("first-account", Now.AddHours(1));
        store.Clear();

        await store.SetAsync("second-account", Now.AddHours(1));

        Assert.Equal("second-account", (await store.GetAsync(TestContext.Current.CancellationToken)).AccessToken);
    }
}
