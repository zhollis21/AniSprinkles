namespace AniSprinkles.PageModels;

/// <summary>
/// A page-lifetime cancellation scope shared by the detail page models, giving them one consistent
/// lifecycle-scoped cancellation model (stop hitting the API once the user navigates away, à la Android's
/// <c>viewModelScope</c> / Swift <c>Task</c> cancellation).
/// <list type="bullet">
/// <item><see cref="Begin"/> starts a fresh scope for a new page load, cancelling any prior one.</item>
/// <item><see cref="EnsureActive"/> hands a follow-on op (Load More / sort) a live token, recreating the
/// scope if it was cancelled while still on the page — e.g. by the CommunityToolkit sort popup's modal
/// <c>OnDisappearing</c>, which would otherwise cancel the very sort it's about to request.</item>
/// <item><see cref="Cancel"/> aborts in-flight work; call it from the page's <c>OnDisappearing</c>.</item>
/// </list>
/// </summary>
public sealed class PageLoadScope : IDisposable
{
    private CancellationTokenSource? _cts;

    /// <summary>Starts a fresh scope for a new load, cancelling and disposing any prior one, and returns
    /// the new token. Use it for the page's main load.</summary>
    public CancellationToken Begin()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    /// <summary>A live token for a follow-on op. Recreates the scope only if it was cancelled while still on
    /// the page, so the op runs on a cancellable token instead of an already-cancelled one.</summary>
    public CancellationToken EnsureActive()
    {
        if (_cts is null || _cts.IsCancellationRequested)
        {
            return Begin();
        }

        return _cts.Token;
    }

    /// <summary>Cancels any in-flight work — call from the page's <c>OnDisappearing</c>.</summary>
    public void Cancel() => _cts?.Cancel();

    public void Dispose() => _cts?.Dispose();
}
