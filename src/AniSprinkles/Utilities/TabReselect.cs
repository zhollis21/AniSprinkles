using Microsoft.Extensions.Logging;

namespace AniSprinkles.Utilities;

/// <summary>
/// What a tap on the already-selected bottom tab does (#200). With pages pushed it pops the tab to
/// its root, leaving the root wherever it was scrolled; at the root it asks the page to scroll to
/// the top. It never refreshes, so a re-tap spends no AniList requests.
/// </summary>
/// <remarks>
/// Lifecycle needs nothing extra here. Pages lower in the stack already had
/// <c>OnNavigatedFrom</c> when the next page was pushed over them, and the pop raises it on the
/// top page, which is where details pages cancel their in-flight loads (#132).
/// </remarks>
public static class TabReselect
{
    // Shell events and the reselect callback both run on the UI thread, so a plain field is enough.
    // It drops a re-tap while a pop is still animating rather than queueing a second pop.
    private static bool _popInProgress;

    public static void Handle(ShellSection section)
    {
        if (_popInProgress)
        {
            return;
        }

        var depth = section.Stack.Count;
        if (depth > 1)
        {
            Log("pop", section, depth);
            // Breadcrumb the pop only: it changes where the user is, so it matters in a crash trace. The
            // scroll changes nothing and would just fill the buffer.
            SentrySdk.AddBreadcrumb($"Tab re-tap popped {section.Route} to root (depth {depth})", "navigation", "user");
            _ = PopToRootAsync(section);
            return;
        }

        var root = (section.CurrentItem as IShellContentController)?.Page;
        Log(root is IScrollsToTop ? "scroll" : "none", section, depth);
        (root as IScrollsToTop)?.ScrollToTop();
    }

    private static async Task PopToRootAsync(ShellSection section)
    {
        _popInProgress = true;
        try
        {
            await section.Navigation.PopToRootAsync(animated: true);
        }
        catch (Exception ex)
        {
            // Fire-and-forget from a platform callback: nothing above this can observe a fault.
            GetLogger()?.LogWarning(ex, "TABRESELECT pop to root failed on {Route}", section.Route);
        }
        finally
        {
            _popInProgress = false;
        }
    }

    private static void Log(string action, ShellSection section, int depth)
        => GetLogger()?.LogInformation(
            "TABRESELECT {Route} depth={Depth} action={Action}", section.Route, depth, action);

    private static ILogger? GetLogger()
        => IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()?.CreateLogger(nameof(TabReselect));
}
