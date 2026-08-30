using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using AndroidX.Core.View;
using AniSprinkles.Platforms.Android;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;
using AndroidColors = Android.Graphics.Color;

namespace AniSprinkles;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.SensorPortrait,
    ConfigurationChanges = 
        ConfigChanges.ScreenSize | 
        ConfigChanges.Orientation | 
        ConfigChanges.UiMode | 
        ConfigChanges.ScreenLayout | 
        ConfigChanges.SmallestScreenSize | 
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // MainActivity uses Android.Util.Log directly rather than ILogger<T>.
    // Lifecycle callbacks fire before the MAUI DI container finishes building,
    // so ILogger resolution here would intermittently return null. The
    // AndroidLogcatLoggerProvider bridges the rest of the app's ILogger output
    // into the same logcat stream, so filtering by tag still works.
    private const string LifecycleTag = "AniSprinklesLifecycle";

    private string ActivityIdentity
        => $"MainActivity[#{GetHashCode():X}]";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Log.Info(LifecycleTag, $"LIFECYCLE {ActivityIdentity} OnCreate (savedInstanceState={(savedInstanceState is null ? "null" : "present")})");

        // A notification tap can arrive here with the process dead, or with the process alive and
        // only the activity destroyed — in which case MAUI's statics may have survived and Shell can
        // already exist. Queue either way and let the drain decide (#111).
        HandleDeepLinkIntent(Intent);
        TryDrainDeepLink();

        // Catch unhandled exceptions from Java/Android side
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
        {
            Log.Error(nameof(MainActivity), $"Unhandled Android exception: {args.Exception}");
            args.Handled = true;
        };

        try
        {
            var window = Window;
            if (window is null)
            {
                return;
            }

            // Enable edge-to-edge drawing: allows content to extend behind system bars.
            // WindowCompat handles all API levels including R+ internally.
            WindowCompat.SetDecorFitsSystemWindows(window, false);

            // Make system bars transparent so app colors show through
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
#pragma warning disable CA1422
                window.SetStatusBarColor(AndroidColors.Transparent);
                window.SetNavigationBarColor(AndroidColors.Transparent);
#pragma warning restore CA1422
            }

            // Set initial window background color after app is initialized
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var backgroundColor = GetWindowBackgroundColor();
                    window.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(new AndroidColors(backgroundColor)));
                    ApplySystemBarIconStyle();
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(MainActivity), $"Error setting initial colors: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainActivity), $"Error in OnCreate: {ex.Message}");
        }
    }

    protected override void OnStart()
    {
        base.OnStart();
        Log.Info(LifecycleTag, $"LIFECYCLE {ActivityIdentity} OnStart");
    }

    /// <summary>
    /// Where a notification tap lands when this activity is already alive — backgrounded or in the
    /// foreground — because of <see cref="LaunchMode.SingleTop"/>.
    /// </summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Log.Info(LifecycleTag, $"LIFECYCLE {ActivityIdentity} OnNewIntent");

        // Android keeps returning the *original* launch intent from the Intent property unless it is
        // replaced, so without this any later read here would see stale extras. Assigned through the
        // property rather than SetIntent, whose API 36 binding requires a second ComponentCaller.
        if (intent is not null)
        {
            Intent = intent;
        }

        HandleDeepLinkIntent(intent);
        TryDrainDeepLink();
    }

    protected override void OnResume()
    {
        base.OnResume();
        Log.Info(LifecycleTag, $"LIFECYCLE {ActivityIdentity} OnResume");

        // Re-reads the intent as well as draining. If queuing failed earlier — DI not yet wired, so
        // ServiceProviderHelper threw — the extras are still on it, and nothing else would ever look
        // at them again within this activity instance. A no-op on the normal path, where the extras
        // were cleared as soon as the link was queued.
        HandleDeepLinkIntent(Intent);

        // Backstop: covers a link queued before Shell existed, when AppShell's Navigated had already
        // fired and won't fire again. Cheap and idempotent when there's nothing pending.
        TryDrainDeepLink();
    }

    // ── Notification deep links (#111) ───────────────────────────────

    /// <summary>
    /// Reads a queued media id off the intent, if there is one, and asks for it to be followed.
    /// Never throws — this runs on lifecycle callbacks where an exception takes the app down.
    /// </summary>
    private void HandleDeepLinkIntent(Intent? intent)
    {
        try
        {
            int mediaId = intent?.GetIntExtra(NotificationHelper.MediaIdExtra, 0) ?? 0;
            if (mediaId <= 0)
            {
                return;
            }

            // Absent, not 0: the nonce comes from a hash, so 0 is a value it can legitimately take.
            int? nonce = intent!.HasExtra(NotificationHelper.NonceExtra)
                ? intent.GetIntExtra(NotificationHelper.NonceExtra, 0)
                : null;

            // Queue before clearing the extras, not after. ServiceProviderHelper throws when DI is
            // not yet wired — a path its own remarks call out — and clearing first would leave the
            // catch below with nothing to recover: no other hook re-reads the Intent, so the link
            // would be gone for good.
            var pending = ServiceProviderHelper.GetServiceProvider().GetRequiredService<PendingDeepLink>();
            if (pending.Set(AppShell.MediaDetailsRoute, new Dictionary<string, object> { ["mediaId"] = mediaId }, nonce))
            {
                Log.Info(LifecycleTag, $"DEEPLINK {ActivityIdentity} queued media {mediaId} (nonce {nonce})");
            }

            // Now that it is queued, clear the extras so an activity recreation reusing this same
            // Intent object cannot re-navigate. That covers the in-memory case; a genuine
            // process-death restore hands back the original extras from the task record, which is
            // what the nonce is for.
            intent.RemoveExtra(NotificationHelper.MediaIdExtra);
            intent.RemoveExtra(NotificationHelper.NonceExtra);
        }
        catch (Exception ex)
        {
            // The extras are still on the intent if this threw before clearing them, so OnResume
            // gets another go once services are up.
            Log.Error(nameof(MainActivity), $"Failed to read deep link intent: {ex}");
        }
    }

    /// <summary>
    /// Follows a queued link if Shell is up. Safe to call repeatedly — three hooks do — because
    /// <see cref="PendingDeepLink"/> only clears once navigation is actually attempted.
    /// </summary>
    private static void TryDrainDeepLink()
    {
        // Dispatched rather than run inline: MAUI navigates on whichever thread calls it, and off the
        // main thread on Android that corrupts the navigation stack instead of throwing
        // (dotnet/maui#13538). Shell.Current is read inside the lambda for the same reason.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var services = IPlatformApplication.Current?.Services;

            // AttemptAsync never throws and treats missing services as "too early", so there is
            // nothing to guard here — a null provider just means the next hook will try again.
            _ = DeepLinkDrain.AttemptAsync(
                services?.GetService<PendingDeepLink>(),
                services?.GetService<INavigationService>(),
                Shell.Current is not null,
                services?.GetService<ILogger<PendingDeepLink>>());
        });
    }

    protected override void OnPause()
    {
        Log.Info(LifecycleTag, $"LIFECYCLE {ActivityIdentity} OnPause");
        base.OnPause();
    }

    protected override void OnStop()
    {
        Log.Info(LifecycleTag, $"LIFECYCLE {ActivityIdentity} OnStop");
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        Log.Info(LifecycleTag, $"LIFECYCLE {ActivityIdentity} OnDestroy (isFinishing={IsFinishing})");
        base.OnDestroy();
    }

    // Fallback color matches the "Background" resource in Resources/Styles/Colors.xaml (#17171A).
    // Used if Application.Current isn't constructed yet or the resource lookup fails, to avoid a
    // pure-black flash that mismatches the rest of the app surface.
    private const int FallbackBackgroundArgb = unchecked((int)0xFF17171A);

    private int GetWindowBackgroundColor()
    {
        try
        {
            var app = Microsoft.Maui.Controls.Application.Current;
            if (app == null)
            {
                return FallbackBackgroundArgb;
            }

            if (app.Resources.TryGetValue("Background", out var colorResource)
                && colorResource is Color mauiColor)
            {
                mauiColor.ToRgba(out byte r, out byte g, out byte b, out byte a);
                return Android.Graphics.Color.Argb(a, r, g, b);
            }

            return FallbackBackgroundArgb;
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainActivity), $"Error getting background color: {ex.Message}");
            return FallbackBackgroundArgb;
        }
    }

    private void ApplySystemBarIconStyle()
    {
        try
        {
            if (Window?.DecorView is not { } decorView)
            {
                return;
            }

            var controller = new WindowInsetsControllerCompat(Window, decorView);
            controller.AppearanceLightStatusBars = false;
            controller.AppearanceLightNavigationBars = false;
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainActivity), $"Error applying icon style: {ex.Message}");
        }
    }
}