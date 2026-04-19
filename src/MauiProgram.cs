using AniSprinkles.Services.Maui;
using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Syncfusion.Maui.Toolkit.Hosting;
#if ANDROID
using AniSprinkles.Platforms.Android;
#endif

namespace AniSprinkles;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSentry(options =>
            {
                options.Dsn = "https://57d120d6c4a16af09b4e71229a8f727c@o4510846094802944.ingest.us.sentry.io/4510846128422912";
                options.SendDefaultPii = false;
                options.TracesSampleRate = 0.0;
                options.Debug = false;
                options.DiagnosticLevel = SentryLevel.Warning;
#if DEBUG
                options.Environment = "Development";
#else
                options.Environment = "Production";
#endif
            })
            .UseMauiCommunityToolkit()
            .ConfigureSyncfusionToolkit()
            .UseFluentIcons()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("SegoeUI-Semibold.ttf", "SegoeSemibold");
            });

        var logDirectory = Path.Combine(FileSystem.Current.AppDataDirectory, "logs");
#if DEBUG
        // Debug builds keep full verbosity for the solo-dev install. Re-evaluate the
        // app-namespace level before first public release.
        var fileLogMinimumLevel = LogLevel.Debug;
        const long fileLogMaxBytes = 1024 * 1024; // 1 MB
        const int fileLogRetainedFiles = 3;
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.Logging.AddFilter("Sentry", LogLevel.Warning);
        builder.Logging.AddDebug();
#else
        // Release builds keep a small on-device ring buffer for user-shared diagnostics.
        // Sentry already captures Warning+ to the cloud; this is the offline fallback and
        // hard-capped so it can't grow silently on long-running installs.
        var fileLogMinimumLevel = LogLevel.Warning;
        const long fileLogMaxBytes = 256 * 1024; // 256 KB
        const int fileLogRetainedFiles = 3;
#endif

        builder.Logging.AddProvider(new FileLoggerProvider(
            logDirectory,
            minimumLevel: fileLogMinimumLevel,
            maxFileSizeBytes: fileLogMaxBytes,
            retainedFiles: fileLogRetainedFiles));
        builder.Logging.AddFilter<FileLoggerProvider>(string.Empty, fileLogMinimumLevel);
        builder.Logging.AddFilter<FileLoggerProvider>("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter<FileLoggerProvider>("System", LogLevel.Warning);
        builder.Logging.AddFilter<FileLoggerProvider>("Sentry", LogLevel.Warning);

#if ANDROID
        // AddDebug() does NOT bridge to logcat on .NET MAUI Android (verified empirically).
        // Without this provider, adb logcat shows nothing from the Microsoft.Extensions.Logging
        // pipeline. Registered for all build configs so device diagnostics work in Release too.
        //
        // Logcat is on-device developer diagnostics — kept at Debug independent of the file
        // logger's per-config minimum. Release file logging stays capped at Warning (Sentry +
        // 256 KB ring buffer), but `adb logcat` should still surface full ILogger output when
        // a dev is attached to a Release build on a test device.
        const LogLevel logcatMinimumLevel = LogLevel.Debug;
        builder.Logging.AddProvider(new AndroidLogcatLoggerProvider(logcatMinimumLevel));
        builder.Logging.AddFilter<AndroidLogcatLoggerProvider>(string.Empty, logcatMinimumLevel);
        builder.Logging.AddFilter<AndroidLogcatLoggerProvider>("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter<AndroidLogcatLoggerProvider>("System", LogLevel.Warning);
        builder.Logging.AddFilter<AndroidLogcatLoggerProvider>("Sentry", LogLevel.Warning);
#endif

        // MAUI auto-registers IDispatcher via UseMauiApp, but IPreferences is only exposed as
        // the static Preferences.Default — DI has no default, so resolving any PageModel that
        // takes IPreferences throws InvalidOperationException at startup. Register it explicitly.
        // TimeProvider is also not auto-registered; adding TryAddSingleton keeps DI-first code
        // paths testable via FakeTimeProvider without forcing tests to discover a default.
        builder.Services.TryAddSingleton<IPreferences>(_ => Preferences.Default);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<INavigationService, MauiShellNavigationService>();
        builder.Services.AddSingleton<ErrorReportService>();
        builder.Services.AddTransient<LoggingHandler>();
        builder.Services.AddSingleton(sp =>
        {
            var handler = sp.GetRequiredService<LoggingHandler>();
            handler.InnerHandler = new HttpClientHandler();
            return new HttpClient(handler);
        });
#if CI
        builder.Services.AddSingleton<IAuthService, CIAuthService>();
        builder.Services.AddSingleton<IAniListClient, CIAniListClient>();
        builder.Services.AddSingleton<IAiringNotificationService, CIAiringNotificationService>();
#elif ERROR_SIM
        builder.Services.AddSingleton<IAuthService, SimAuthService>();
        builder.Services.AddSingleton<IAniListClient, FailingAniListClient>();
        builder.Services.AddSingleton<IAiringNotificationService, AiringNotificationService>();
#else
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<IAniListClient, AniListClient>();
        builder.Services.AddSingleton<IAiringNotificationService, AiringNotificationService>();
#endif
        builder.Services.AddSingleton<MyAnimePageModel>();
        builder.Services.AddTransient<MyAnimePage>();
        builder.Services.AddSingleton<SettingsPageModel>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<MediaDetailsPageModel>();
        builder.Services.AddTransient<MediaDetailsPage>();

        return builder.Build();
    }
}
