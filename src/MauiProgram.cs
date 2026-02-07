using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Sentry.Maui;
using Syncfusion.Maui.Toolkit.Hosting;
using System.Net.Http;

namespace AniSprinkles
{
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
#if DEBUG
                    options.Environment = "Development";
                    options.Debug = true;
#else
                    options.Environment = "Production";
#endif
                })
                .UseMauiCommunityToolkit()
                .ConfigureSyncfusionToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("SegoeUI-Semibold.ttf", "SegoeSemibold");
                    fonts.AddFont("FluentSystemIcons-Regular.ttf", FluentUI.FontFamily);
                });

#if DEBUG
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
            builder.Services.AddSingleton<IAuthService, AuthService>();
            builder.Services.AddSingleton<IAniListClient, AniListClient>();
            builder.Services.AddTransient<MyAnimePageModel>();
            builder.Services.AddTransient<MyAnimePage>();
            builder.Services.AddTransient<SettingsPageModel>();
            builder.Services.AddTransient<SettingsPage>();
            builder.Services.AddTransient<MediaDetailsPageModel>();
            builder.Services.AddTransient<MediaDetailsPage>();

            return builder.Build();
        }
    }
}
