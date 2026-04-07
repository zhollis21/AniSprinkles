using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles;

public partial class App : Application
{
    private readonly IAuthService _authService;
    private readonly IAniListClient _aniListClient;
    private readonly ILogger<App> _logger;

    public App(IAuthService authService, IAniListClient aniListClient, ILogger<App> logger)
    {
        InitializeComponent();
        AppSettings.Load();

        _authService = authService;
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
    /// </summary>
    private async Task SyncSettingsFromAniListAsync()
    {
        try
        {
            var token = await _authService.GetAccessTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                return;
            }

            var viewer = await _aniListClient.GetViewerAsync().ConfigureAwait(false);
            AppSettings.SyncFromViewer(viewer);
            _logger.LogInformation("Synced settings from AniList on startup");
        }
        catch (Exception ex)
        {
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