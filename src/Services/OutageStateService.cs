using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Services;

/// <summary>
/// Singleton implementation of <see cref="IOutageStateService"/>. <see cref="IsOutage"/>
/// is sticky while in the outage state and only clears when a subsequent successful API
/// call arrives, preventing banner flapping during partial outages.
///
/// Thread model: callers come from any thread (HTTP continuations run on pool threads
/// because <c>AniListClient.SendAsync</c> uses <c>ConfigureAwait(false)</c>). Property
/// writes are marshaled to the main thread via <c>MainThread</c> so XAML bindings
/// update on the UI thread. The dispatcher queue also serializes concurrent
/// success/failure reports, so no explicit lock is needed.
/// </summary>
public partial class OutageStateService : ObservableObject, IOutageStateService
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

        // Snapshot the message fields before dispatching so the lambda captures value types
        // rather than the exception reference.
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
        if (!IsOutage)
        {
            return;
        }

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

    private static void DispatchOrInvoke(Action action)
    {
        // MainThread is unavailable until a MauiApp is running (unit tests, early startup),
        // so fall back to an inline invoke in those cases. In production this always
        // marshals to the UI thread.
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        try
        {
            MainThread.BeginInvokeOnMainThread(action);
        }
        catch (InvalidOperationException)
        {
            // No dispatcher yet — happens during early test-host bootstrap. Run inline.
            action();
        }
    }
}
