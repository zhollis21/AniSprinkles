using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Services;

/// <summary>
/// Singleton implementation of <see cref="IOutageStateService"/>. <see cref="IsOutage"/>
/// is sticky while in the outage state and only clears when a subsequent successful API
/// call arrives, preventing banner flapping during partial outages.
///
/// Thread model: callers come from any thread (HTTP continuations run on pool threads
/// because <c>AniListClient.SendAsync</c> uses <c>ConfigureAwait(false)</c>). Property
/// writes are marshaled to the main thread via the injected <see cref="IDispatcher"/> so XAML
/// bindings update on the UI thread. The dispatcher queue also serializes concurrent
/// success/failure reports, so no explicit lock is needed.
/// </summary>
public partial class OutageStateService(IDispatcher dispatcher) : ObservableObject, IOutageStateService
{
    [ObservableProperty]
    private bool _isOutage;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _iconGlyph = string.Empty;

    public void ReportFailure(Exception ex)
    {
        if (ex is not AniListApiException { Kind: ApiErrorKind.ServiceOutage } apiEx)
        {
            return;
        }

        // Snapshot the message strings before dispatching so the lambda doesn't retain
        // the exception instance longer than necessary.
        var title = apiEx.UserTitle;
        var subtitle = apiEx.UserSubtitle;
        var icon = apiEx.IconGlyph;

        DispatchOrInvoke(() =>
        {
            Title = title;
            Subtitle = subtitle;
            IconGlyph = icon;
            IsOutage = true;
        });
    }

    public void ReportSuccess()
    {
        // Always dispatch so we can't race with a concurrent ReportFailure that has
        // scheduled `IsOutage = true` but not yet run on the UI thread. The pre-dispatch
        // check would otherwise let a success slip through silently, leaving the banner
        // stuck on. The inner check still skips the no-op write when not in outage.
        DispatchOrInvoke(() =>
        {
            if (!IsOutage)
            {
                return;
            }

            IsOutage = false;
            Title = string.Empty;
            Subtitle = string.Empty;
            IconGlyph = string.Empty;
        });
    }

    private void DispatchOrInvoke(Action action)
    {
        // Was MainThread.IsMainThread, which throws NotImplementedInReferenceAssemblyException on the
        // plain net10.0 TFM this library now targets. The old catch only covered
        // InvalidOperationException, so the "falls back inline in unit tests" the comment promised
        // never actually happened — the throw escaped. IDispatcher is injected instead, and a test
        // can supply one that runs inline.
        // Dispatch returns false when it could not queue the work (no dispatcher loop yet — the
        // early-bootstrap case the old catch was reaching for). Running inline is better than
        // dropping the state change, which would strand the outage banner.
        if (dispatcher.IsDispatchRequired && dispatcher.Dispatch(action))
        {
            return;
        }

        action();
    }
}
