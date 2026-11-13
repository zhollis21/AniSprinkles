using AniSprinkles.PageModels;

namespace AniSprinkles.UnitTests;

public class PageLoadScopeTests
{
    [Test]
    public void Begin_ReturnsLiveToken()
    {
        using var scope = new PageLoadScope();

        var token = scope.Begin();

        Assert.That(token.IsCancellationRequested, Is.False);
    }

    [Test]
    public void Cancel_CancelsTheCurrentToken()
    {
        using var scope = new PageLoadScope();
        var token = scope.Begin();

        scope.Cancel();

        Assert.That(token.IsCancellationRequested, Is.True);
    }

    [Test]
    public void Begin_CancelsThePreviousScope()
    {
        using var scope = new PageLoadScope();

        var first = scope.Begin();
        var second = scope.Begin();

        Assert.That(first.IsCancellationRequested, Is.True);   // a new load aborts the prior one
        Assert.That(second.IsCancellationRequested, Is.False);
    }

    [Test]
    public void EnsureActive_WhenLive_ReusesTheSameToken()
    {
        using var scope = new PageLoadScope();
        var begun = scope.Begin();

        var ensured = scope.EnsureActive();

        Assert.That(ensured, Is.EqualTo(begun));                 // not recreated while still live
        Assert.That(ensured.IsCancellationRequested, Is.False);
    }

    [Test]
    public void EnsureActive_AfterCancel_ReturnsAFreshLiveToken()
    {
        using var scope = new PageLoadScope();
        scope.Begin();
        scope.Cancel();                               // e.g. the sort popup's OnDisappearing

        var token = scope.EnsureActive();

        Assert.That(token.IsCancellationRequested, Is.False);  // recreated so the follow-on op can run
    }

    [Test]
    public void Cancel_AfterEnsureActiveRecreated_CancelsTheNewToken()
    {
        using var scope = new PageLoadScope();
        scope.Begin();
        scope.Cancel();
        var token = scope.EnsureActive();             // fresh scope after the popup cycle

        scope.Cancel();                               // a real navigate-away

        Assert.That(token.IsCancellationRequested, Is.True);   // still cancellable on nav-away
    }

    [Test]
    public void EnsureActive_WithoutBegin_StartsAScope()
    {
        using var scope = new PageLoadScope();

        var token = scope.EnsureActive();

        Assert.That(token.IsCancellationRequested, Is.False);
    }
}
