namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// An <see cref="IDispatcher"/> that runs work inline on the calling thread. Page models marshal
/// UI-affecting writes through the dispatcher; off-device there is no UI thread to marshal to, and
/// MAUI's own <c>MainThread</c> helpers throw <c>NotImplementedInReferenceAssemblyException</c> on
/// the plain <c>net10.0</c> TFM — injecting this is what makes those paths observable.
/// </summary>
public sealed class ImmediateDispatcher : IDispatcher
{
    public bool IsDispatchRequired => false;

    public bool Dispatch(Action action)
    {
        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        action();
        return true;
    }

    public IDispatcherTimer CreateTimer() => throw new NotSupportedException(
        "No test needs a dispatcher timer yet; add one here when the first does.");
}
