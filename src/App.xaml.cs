using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles;

public partial class App : Application
{
    private readonly IAniListClient _aniListClient;
    private readonly ILogger<App> _logger;

    public App(IAniListClient aniListClient, ILogger<App> logger)
    {
        InitializeComponent();
        AppSettings.Load();

        _aniListClient = aniListClient;
        _logger = logger;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                ShowCrashAlert(ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            ShowCrashAlert(e.Exception);
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _ = SyncSettingsFromAniListAsync();
        return new Window(new AppShell());
    }

    /// <summary>
    /// Syncs local AppSettings from AniList on every app launch so that
    /// cross-device setting changes are picked up immediately.
    /// LastSyncedUtc is stamped before the network call so that MyAnimePageModel.LoadAsync,
    /// which can run concurrently, sees the in-progress sync and skips its own viewer fetch.
    /// </summary>
    private async Task SyncSettingsFromAniListAsync()
    {
        // Stamp before the await so concurrent LoadAsync calls see it immediately
        // and skip their own viewer fetch rather than racing with this one.
        AppSettings.MarkSyncStarted();
        try
        {
            var viewer = await _aniListClient.GetViewerAsync().ConfigureAwait(false);
            AppSettings.SyncFromViewer(viewer);
            _logger.LogInformation("Synced settings from AniList on startup");
        }
        catch (AniListApiException ex) when (ex.Kind == ApiErrorKind.Authentication)
        {
            // Not signed in — reset the timestamp so the next load syncs normally.
            AppSettings.ClearSyncTimestamp();
        }
        catch (Exception ex)
        {
            AppSettings.ClearSyncTimestamp();
            _logger.LogWarning(ex, "Failed to sync settings from AniList on startup");
        }
    }

    private static void ShowCrashAlert(Exception ex)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var page = Current?.Windows.FirstOrDefault()?.Page;
                if (page is null)
                {
                    return;
                }

                var message = $"{ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException is not null)
                {
                    message += $"\n\nInner: {ex.InnerException.Message}";
                }

                await page.DisplayAlertAsync("Crash Detected", message, "OK");
            }
            catch
            {
                // Avoid secondary exceptions in the crash handler
            }
        });
    }
}