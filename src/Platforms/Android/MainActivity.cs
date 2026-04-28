using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using AndroidX.Core.View;
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

    protected override void OnResume()
    {
        base.OnResume();
        Log.Info(LifecycleTag, $"LIFECYCLE {ActivityIdentity} OnResume");
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

    private int GetWindowBackgroundColor()
    {
        try
        {
            var app = Microsoft.Maui.Controls.Application.Current;
            if (app == null)
            {
                return AndroidColors.Black;
            }

            if (app.Resources.TryGetValue("Background", out var colorResource)
                && colorResource is Color mauiColor)
            {
                mauiColor.ToRgba(out byte r, out byte g, out byte b, out byte a);
                return Android.Graphics.Color.Argb(a, r, g, b);
            }

            return AndroidColors.Black;
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainActivity), $"Error getting background color: {ex.Message}");
            return AndroidColors.Black;
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