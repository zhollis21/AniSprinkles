using Microsoft.Extensions.Logging;
using Syncfusion.Maui.Toolkit.Hosting;

namespace AniSprinkles
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
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
            builder.Services.AddLogging(configure => configure.AddDebug());
#endif

            builder.Services.AddSingleton<IAuthService, MockAuthService>();
            builder.Services.AddSingleton<IAniListClient, MockAniListClient>();
            builder.Services.AddTransient<PageModels.MyAnimePageModel>();
            builder.Services.AddTransient<Pages.MyAnimePage>();

            return builder.Build();
        }
    }
}
