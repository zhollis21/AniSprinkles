using AniSprinkles.Utilities;

namespace AniSprinkles;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        AppSettings.Load();

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