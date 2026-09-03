using AniSprinkles.Utilities;

namespace AniSprinkles;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        AppSettings.Load();

        UserAppTheme = AppTheme.Dark;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // Flushed first, before anything that could itself fail. This is the one handler that
            // cannot keep the process alive — there is no Handled flag to set — so whatever the ring
            // holds reaches disk here or not at all (#112).
            DiagnosticsFlush.Flush();

            if (e.ExceptionObject is Exception ex)
            {
                ShowCrashAlert(ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticsFlush.Flush();
            e.SetObserved();
            ShowCrashAlert(e.Exception);
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
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