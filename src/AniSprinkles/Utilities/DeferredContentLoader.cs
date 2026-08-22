using Microsoft.Extensions.Logging;

namespace AniSprinkles.Utilities;

/// <summary>
/// Shared deferred-load + loaded-content-host machinery for the details pages. It owns the two
/// fiddly, race-prone mechanisms every detail page repeated verbatim:
///
/// <list type="number">
/// <item><b>Deferred load scheduling</b> — a monotonic version counter so the heavy load only runs
/// after the page has appeared and yielded a frame (keeping the Shell transition smooth), and so a
/// rapid re-navigation or a navigate-away supersedes an in-flight schedule.</item>
/// <item><b>Loaded-content host swap</b> — lazily creating the heavy "loaded" view into a host
/// <see cref="ContentView"/> once data is ready, tearing it down (with full handler disconnect) when
/// the page leaves or the target identity changes, and falling back to an error state if the view
/// fails to inflate.</item>
/// </list>
///
/// Each page composes one of these and supplies the entity-specific bits via callbacks. MAUI-coupled
/// (it touches <see cref="ContentView"/> and handlers), so it is UI glue rather than tested logic —
/// the tested logic (list ops) lives in <see cref="AniSprinkles.PageModels.ListOperationRunner"/>.
/// </summary>
public sealed class DeferredContentLoader(
    ILogger logger,
    ContentView host,
    string entityName,
    Func<bool> shouldShowContent,
    Func<View> createView,
    Action<Exception> onRenderError)
{
    private bool _hasAppeared;
    private bool _hasCreatedContent;
    private int _pendingVersion;
    private int _scheduledVersion;

    /// <summary>True between OnAppearing and OnDisappearing — a scheduled load checks this after its yield.</summary>
    public bool IsCurrent(int version) => _hasAppeared && version == _pendingVersion;

    /// <summary>
    /// Tears down the loaded content when navigating to a different target (or before the first load).
    /// On back-navigation Shell re-applies the same query, so keeping the view avoids a costly
    /// XAML re-inflation; only an identity change (or no content yet) forces a rebuild.
    /// </summary>
    public void ResetContentIfStale(bool identityChanged)
    {
        if (identityChanged || !_hasCreatedContent)
        {
            DetachContent();
        }
    }

    /// <summary>Invalidates any in-flight schedule (call after recording a new pending request).</summary>
    public void BumpVersion() => _pendingVersion++;

    public void OnAppearing()
    {
        _hasAppeared = true;
        UpdateHost();
    }

    public void OnDisappearing()
    {
        _hasAppeared = false;
        _pendingVersion++; // supersede any scheduled/in-flight load so it no-ops after the yield
    }

    /// <summary>
    /// Schedules <paramref name="runLoad"/> once per pending version. <paramref name="runLoad"/>
    /// receives the captured version and must re-check <see cref="IsCurrent"/> after it yields.
    /// </summary>
    public void TrySchedule(Func<int, Task> runLoad)
    {
        if (!_hasAppeared || _pendingVersion == _scheduledVersion)
        {
            return;
        }

        var version = _pendingVersion;
        _scheduledVersion = version;

        runLoad(version).ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    logger.LogError(task.Exception, "{Entity} deferred load faulted", entityName);
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Attaches the loaded view once content is ready, or detaches it otherwise.</summary>
    public void UpdateHost()
    {
        if (shouldShowContent())
        {
            if (!_hasCreatedContent)
            {
                logger.LogInformation("LOADEDHOST {Entity} attach", entityName);
                try
                {
                    // Keep the first navigation frame lightweight: build the heavy subtree only after
                    // data has loaded, so the user sees the loading page instantly.
                    host.Content = createView();
                    _hasCreatedContent = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create {Entity} loaded content view", entityName);
                    onRenderError(ex);
                }
            }
        }
        else if (_hasCreatedContent)
        {
            logger.LogInformation("LOADEDHOST {Entity} detach", entityName);
            DetachContent();
        }
    }

    private void DetachContent()
    {
        HandlerHelper.DisconnectAll(host.Content);
        host.Content = null;
        _hasCreatedContent = false;
    }
}
