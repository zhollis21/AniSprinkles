using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Syncfusion.Maui.Toolkit.Hosting;

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

#if DEBUG
        var logDirectory = Path.Combine(FileSystem.Current.AppDataDirectory, "logs");
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.Logging.AddFilter("Sentry", LogLevel.Warning);
        builder.Logging.AddProvider(new FileLoggerProvider(logDirectory, minimumLevel: LogLevel.Information));
        builder.Logging.AddFilter<FileLoggerProvider>(string.Empty, LogLevel.Information);
        builder.Logging.AddFilter<FileLoggerProvider>("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter<FileLoggerProvider>("System", LogLevel.Warning);
        builder.Logging.AddFilter<FileLoggerProvider>("Sentry", LogLevel.Warning);
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<ErrorReportService>();
        builder.Services.AddTransient<LoggingHandler>();
        builder.Services.AddSingleton(sp =>
        {
            var handler = sp.GetRequiredService<LoggingHandler>();
            handler.InnerHandler = new HttpClientHandler();
            return new HttpClient(handler);
        });
#if CI
        builder.Services.AddSingleton<IAuthService, CiAuthService>();
        builder.Services.AddSingleton<IAniListClient, CiAniListClient>();
#else
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<IAniListClient, AniListClient>();
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
