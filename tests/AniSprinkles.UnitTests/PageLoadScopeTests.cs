using AniSprinkles.PageModels;

namespace AniSprinkles.UnitTests;

public class PageLoadScopeTests
{
    [Fact]
    public void Begin_ReturnsLiveToken()
    {
        using var scope = new PageLoadScope();

        var token = scope.Begin();

        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_CancelsTheCurrentToken()
    {
        using var scope = new PageLoadScope();
        var token = scope.Begin();

        scope.Cancel();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Begin_CancelsThePreviousScope()
    {
        using var scope = new PageLoadScope();

        var first = scope.Begin();
        var second = scope.Begin();

        Assert.True(first.IsCancellationRequested);   // a new load aborts the prior one
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void EnsureActive_WhenLive_ReusesTheSameToken()
    {
        using var scope = new PageLoadScope();
        var begun = scope.Begin();

        var ensured = scope.EnsureActive();

        Assert.Equal(begun, ensured);                 // not recreated while still live
        Assert.False(ensured.IsCancellationRequested);
    }

    [Fact]
    public void EnsureActive_AfterCancel_ReturnsAFreshLiveToken()
    {
        using var scope = new PageLoadScope();
        scope.Begin();
        scope.Cancel();                               // e.g. the sort popup's OnDisappearing

        var token = scope.EnsureActive();

        Assert.False(token.IsCancellationRequested);  // recreated so the follow-on op can run
    }

    [Fact]
    public void Cancel_AfterEnsureActiveRecreated_CancelsTheNewToken()
    {
        using var scope = new PageLoadScope();
        scope.Begin();
        scope.Cancel();
        var token = scope.EnsureActive();             // fresh scope after the popup cycle

        scope.Cancel();                               // a real navigate-away

        Assert.True(token.IsCancellationRequested);   // still cancellable on nav-away
    }

    [Fact]
    public void EnsureActive_WithoutBegin_StartsAScope()
    {
        using var scope = new PageLoadScope();

        var token = scope.EnsureActive();

        Assert.False(token.IsCancellationRequested);
    }
}
