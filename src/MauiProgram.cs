using AniSprinkles.Services.Maui;
using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Syncfusion.Maui.Toolkit.Hosting;
#if DEBUG
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
#endif
#if ANDROID
using AniSprinkles.Platforms.Android;
#endif

namespace AniSprinkles;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
#if DEBUG
        // Diagnostic: surface Aspire-injected env vars in logcat so we can
        // verify the Android @(AndroidEnvironment) targets file actually
        // reached the APK and the Mono runtime set the vars. Console.WriteLine
        // from an Android app process is NOT captured by the Aspire dashboard
        // (the AppHost launches via adb and doesn't pipe stdout), so we log
        // to logcat directly and read with `adb logcat -s AniSprinkles:*`.
        var aspireOtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var aspireOtlpProtocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");
        var aspireServiceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
#if ANDROID
        Android.Util.Log.Info("AniSprinkles", $"[Aspire] OTEL_EXPORTER_OTLP_ENDPOINT={aspireOtlpEndpoint ?? "<null>"}");
        Android.Util.Log.Info("AniSprinkles", $"[Aspire] OTEL_EXPORTER_OTLP_PROTOCOL={aspireOtlpProtocol ?? "<null>"}");
        Android.Util.Log.Info("AniSprinkles", $"[Aspire] OTEL_SERVICE_NAME={aspireServiceName ?? "<null>"}");
#endif

        // MauiAppBuilder.Configuration does NOT auto-include OS environment
        // variables (unlike Host.CreateApplicationBuilder). Aspire injects
        // OTEL_EXPORTER_OTLP_ENDPOINT into the emulator process via an
        // Android @(AndroidEnvironment) targets file, but without this call
        // AddServiceDefaults → AddOpenTelemetryExporters reads it as null and
        // UseOtlpExporter() never runs → dashboard shows no traces/logs.
        // Must run before AddServiceDefaults.
        builder.Configuration.AddEnvironmentVariables();

        // Aspire service defaults: OpenTelemetry (traces/metrics/logs), service
        // discovery, and standard HTTP resilience. Debug-only — Release builds
        // ship Sentry-only telemetry and never reference the service-defaults
        // project (see conditional ProjectReference in AniSprinkles.csproj).
        // The OTLP exporter inside AddServiceDefaults is a no-op unless
        // OTEL_EXPORTER_OTLP_ENDPOINT is injected by the Aspire AppHost, so
        // direct F5 (no AppHost) remains safe.
        builder.AddServiceDefaults();

        // App-specific meters. Keep this in MauiProgram (not ServiceDefaults)
        // so the service-defaults template stays reusable. AniListRateLimitHandler
        // publishes anilist.requests, anilist.ratelimit.remaining, and
        // anilist.ratelimit.throttled on this meter.
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter("AniSprinkles.AniList"));
#endif
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
        // Per-provider filters only — global filters would cascade to the
        // OpenTelemetry logger provider and strip Information-level logs
        // (e.g. HttpClient request logs) from the Aspire dashboard.
        builder.Logging.AddFilter<Microsoft.Extensions.Logging.Debug.DebugLoggerProvider>("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter<Microsoft.Extensions.Logging.Debug.DebugLoggerProvider>("System", LogLevel.Warning);
        builder.Logging.AddFilter<Microsoft.Extensions.Logging.Debug.DebugLoggerProvider>("Sentry", LogLevel.Warning);
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
        builder.Services.AddTransient<AniListRateLimitHandler>();
        // Named client "anilist". ConfigureHttpClientDefaults from AddServiceDefaults
        // (Debug only) layers the standard resilience handler + service discovery
        // on top. In Release only the handlers registered here run — rate-limit
        // observation + Sentry-breadcrumb logging — keeping the Release surface
        // aligned with today's behaviour minus the stopwatch-only path.
        //
        // Handler order (outermost → innermost → network):
        //   [StandardResilienceHandler (Debug via defaults)]
        //     → AniListRateLimitHandler
        //       → LoggingHandler
        //         → primary HttpClientHandler
        builder.Services.AddHttpClient("anilist")
            .AddHttpMessageHandler<AniListRateLimitHandler>()
            .AddHttpMessageHandler<LoggingHandler>();
        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("anilist"));
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
