using AniSprinkles.Services.Maui;
using CommunityToolkit.Maui;
#if DEBUG
using AniSprinkles.Services.FaultInjection;
#endif
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

                // Defence in depth for #124. AniListClient already redacts server-derived text
                // before it reaches an exception message, but Sentry captures unhandled exceptions
                // by a path that goes through neither it nor ErrorReportService, so every outbound
                // event gets one last pass. SendDefaultPii above governs IP/username, not message
                // content — it would not have caught a token echoed in an error body.
                options.SetBeforeSend((evt, _) => SentryScrubber.Scrub(evt));
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

        // MAUI auto-registers IDispatcher via UseMauiApp, but IPreferences and IAppInfo are only
        // exposed as the statics Preferences.Default / AppInfo.Current — DI has no default, so
        // resolving a PageModel that takes either throws InvalidOperationException at startup.
        // TimeProvider is also not auto-registered; adding TryAddSingleton keeps DI-first code
        // paths testable via FakeTimeProvider without forcing tests to discover a default.
        builder.Services.TryAddSingleton<IPreferences>(_ => Preferences.Default);
        builder.Services.TryAddSingleton<IAppInfo>(_ => AppInfo.Current);
        builder.Services.TryAddSingleton(TimeProvider.System);
        // The Core library's seams onto this project's MAUI-only implementations. Everything a page
        // model needs that touches Shell, CommunityToolkit popups, or Essentials goes through one of
        // these, which is what lets the page models run on the plain net10.0 test TFM (issue #62).
        builder.Services.AddSingleton<INavigationService, MauiShellNavigationService>();
        builder.Services.AddSingleton<ISecureTokenStorage, MauiSecureTokenStorage>();
        builder.Services.AddSingleton<IUserFeedback, MauiUserFeedback>();
        builder.Services.AddSingleton<IDialogService, MauiDialogService>();
        builder.Services.AddSingleton<IExternalBrowser, MauiExternalBrowser>();
        builder.Services.AddSingleton<IOutageStateService, OutageStateService>();
        builder.Services.AddSingleton<ListEntryStatusFlow>();
        builder.Services.AddSingleton<ErrorReportService>();
        builder.Services.AddTransient<LoggingHandler>();
        builder.Services.AddTransient<AniListRateLimitHandler>();
#if DEBUG
        // Fault injection (#125). One state shared by both seams — the IAniListClient decorator and
        // the HTTP handler below — so a profile names which layer it fires at and only fires there.
        // Disarmed until an adb broadcast arms it, which is what makes it safe to compile into every
        // Debug build and is why -p:ErrorSim=true is gone.
        builder.Services.AddSingleton<FaultState>();
#endif
        builder.Services.AddSingleton(sp =>
        {
            // Pipeline (outermost first): rate-limit gate → logging → [fault] → network. The gate is
            // outermost so each retried attempt still flows through LoggingHandler and gets logged
            // individually.
            HttpMessageHandler network = new HttpClientHandler();
#if DEBUG
            // Innermost, deliberately: everything above it then treats a synthetic answer exactly as
            // if it had come off the wire, which is what lets AniListRateLimitHandler's Retry-After
            // path and AniListErrorClassifier run for real against an injected 429 or 503 (#125).
            network = new FaultInjectingHttpHandler(
                sp.GetRequiredService<FaultState>(),
                sp.GetRequiredService<ILogger<FaultInjectingHttpHandler>>())
            {
                InnerHandler = network,
            };
#endif
            var logging = sp.GetRequiredService<LoggingHandler>();
            logging.InnerHandler = network;
            var rateLimit = sp.GetRequiredService<AniListRateLimitHandler>();
            rateLimit.InnerHandler = logging;
            return new HttpClient(rateLimit);
        });
#if CI
        builder.Services.AddSingleton<IAuthService, CIAuthService>();
        builder.Services.AddSingleton<CIAniListClient>();
        builder.Services.AddSingleton<IAiringNotificationService, CIAiringNotificationService>();
#else
        // Singleton alongside AuthService, which is the only thing that resolves it: TokenStore holds
        // the process-wide token and the gate that single-flights its first read (#119). A transient
        // one would give every caller its own gate and its own copy, which is the bug it fixes.
        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<AniListClient>();
        builder.Services.AddSingleton<IAiringNotificationService, AiringNotificationService>();
#endif
        // The one IAniListClient everything resolves, built here rather than registered directly so
        // the fault decorator can wrap whichever implementation this configuration selected (#125).
        // Wrapping rather than replacing is the whole point: the CI fixtures still serve every call
        // the profile does not target, so a real screen loads and then the next call breaks — which
        // is what ErrorSim's all-methods-throw stub made impossible.
        builder.Services.AddSingleton<IAniListClient>(sp =>
        {
#if CI
            IAniListClient client = sp.GetRequiredService<CIAniListClient>();
#else
            IAniListClient client = new CachingAniListClient(
                sp.GetRequiredService<AniListClient>(),
                sp.GetRequiredService<ILogger<CachingAniListClient>>());
#endif
#if DEBUG
            client = new FaultInjectingAniListClient(
                client,
                sp.GetRequiredService<FaultState>(),
                sp.GetRequiredService<IOutageStateService>(),
                sp.GetRequiredService<ILogger<FaultInjectingAniListClient>>());
#endif
            return client;
        });
        builder.Services.AddSingleton<MyAnimePageModel>();
        builder.Services.AddTransient<MyAnimePage>();
        builder.Services.AddSingleton<DiscoverPageModel>();
        builder.Services.AddTransient<DiscoverPage>();
        builder.Services.AddSingleton<SearchPageModel>();
        builder.Services.AddTransient<SearchPage>();
        // Placeholder pages: no PageModel until their features land (manga #12, feed #14).
        builder.Services.AddTransient<FeedPage>();
        builder.Services.AddTransient<MyMangaPage>();
        builder.Services.AddSingleton<SettingsPageModel>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<MediaDetailsPageModel>();
        builder.Services.AddTransient<MediaDetailsPage>();
        builder.Services.AddTransient<StaffDetailsPageModel>();
        builder.Services.AddTransient<StaffDetailsPage>();
        builder.Services.AddTransient<CharacterDetailsPageModel>();
        builder.Services.AddTransient<CharacterDetailsPage>();
        builder.Services.AddTransient<StudioDetailsPageModel>();
        builder.Services.AddTransient<StudioDetailsPage>();
        builder.Services.AddTransient<MediaBrowsePageModel>();
        builder.Services.AddTransient<MediaBrowsePage>();

#if ANDROID
        // Android paints a focus highlight (a stray blue outline) on a CollectionView's RecyclerView when a
        // modal popup is pushed over it and focus shifts — it shows up behind the sort picker. Turn the
        // highlight off for every CollectionView app-wide so it can't bleed behind any popup. This is purely
        // cosmetic (it doesn't change focusability or touch handling), so it's safe across all lists.
        // DefaultFocusHighlightEnabled is API 26+; the app's min SDK is well above that.
        Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping(
            "AniSprinkles.DisableFocusHighlight",
            static (handler, _) =>
            {
                if (handler.PlatformView is Android.Views.View view)
                {
                    view.DefaultFocusHighlightEnabled = false;
                }
            });

#endif

        var app = builder.Build();

#if ANDROID
        // Markdown links in character and staff bios rendered as invisible gaps (#137). Html.FromHtml
        // turns each anchor into a URLSpan, which paints itself with the TextView's textColorLink
        // rather than the label's TextColor — and that resolves through the theme to colorAccent,
        // which colors.xml sets to transparent so the rainbow theming can show through. BioLinkSpans
        // swaps in spans that paint themselves, which sidesteps the theme attribute instead of
        // overriding it, and makes the links tappable while it is there.
        //
        // Registered under a custom key, not nameof(ILabel.Text). A key matching a MAUI property key
        // gets shadowed here: Label.RemapForControls swaps LabelHandler.Mapper for a
        // ControlsLabelMapper that defines Text and TextType itself, and that runs off Label's
        // static init during the first page render — after this method returns either way. Both a
        // pre-Build and a post-Build registration on those keys were silently dropped on device,
        // links still invisible, no error anywhere. Nothing shadows a custom key, which is why the
        // CollectionView customization above works.
        //
        // The tradeoff is that a custom key runs only when the handler is built, so it cannot catch
        // the spoiler toggle rewriting BioProse. BioLinkSpans.Attach hangs a TextWatcher off the
        // view for that.
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(
            "AniSprinkles.BioLinks",
            static (handler, label) =>
            {
                if (label is Label { TextType: TextType.Html }
                    && handler.PlatformView is Android.Widget.TextView textView)
                {
                    BioLinkSpans.Attach(textView);
                }
            });
#endif

        return app;
    }
}
