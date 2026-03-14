using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using AndroidX.Core.View;
using AndroidColors = Android.Graphics.Color;
using Microsoft.Maui.Graphics;

namespace AniSprinkles;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true, 
    LaunchMode = LaunchMode.SingleTop, 
    ConfigurationChanges = 
        ConfigChanges.ScreenSize | 
        ConfigChanges.Orientation | 
        ConfigChanges.UiMode | 
        ConfigChanges.ScreenLayout | 
        ConfigChanges.SmallestScreenSize | 
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

#if DEBUG
        SeedCiAuthToken();
#endif

        // Catch unhandled exceptions from Java/Android side
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
        {
            Log.Error(nameof(MainActivity), $"Unhandled Android exception: {args.Exception}");
            args.Handled = true;
        };

        try
        {
            var window = Window;

            // Enable edge-to-edge drawing: allows content to extend behind system bars
            WindowCompat.SetDecorFitsSystemWindows(window, false);

            // Make system bars transparent so app colors show through
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
#pragma warning disable CA1422
                window.SetStatusBarColor(AndroidColors.Transparent);
                window.SetNavigationBarColor(AndroidColors.Transparent);
#pragma warning restore CA1422
            }

            // Allow system window fitting on Android 11+
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                window.SetDecorFitsSystemWindows(false);
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

            // Update colors when theme changes
            Microsoft.Maui.Controls.Application.Current!.RequestedThemeChanged += (_, __) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        var newColor = GetWindowBackgroundColor();
                        window.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(new AndroidColors(newColor)));
                        ApplySystemBarIconStyle();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(nameof(MainActivity), $"Error updating colors on theme change: {ex.Message}");
                    }
                });
            };
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainActivity), $"Error in OnCreate: {ex.Message}");
        }
    }

    private int GetWindowBackgroundColor()
    {
        try
        {
            var app = Microsoft.Maui.Controls.Application.Current;
            if (app == null)
            {
                return AndroidColors.White;
            }

            // Get the appropriate background color based on theme
            var isDarkTheme = app.RequestedTheme == AppTheme.Dark;
            string colorKey = isDarkTheme ? "DarkBackground" : "LightBackground";

            if (app.Resources.TryGetValue(colorKey, out var colorResource))
            {
                if (colorResource is Color mauiColor)
                {
                    // Convert MAUI Color to Android int
                    mauiColor.ToRgba(out byte r, out byte g, out byte b, out byte a);
                    return Android.Graphics.Color.Argb(a, r, g, b);
                }
            }

            return AndroidColors.White;
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainActivity), $"Error getting background color: {ex.Message}");
            return AndroidColors.White;
        }
    }

    private void ApplySystemBarIconStyle()
    {
        try
        {
            var isDark = Microsoft.Maui.Controls.Application.Current!.RequestedTheme == AppTheme.Dark;
            var controller = new WindowInsetsControllerCompat(Window, Window.DecorView);
            controller.AppearanceLightStatusBars = !isDark;       // dark icons on light background
            controller.AppearanceLightNavigationBars = !isDark;   // if supported
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainActivity), $"Error applying icon style: {ex.Message}");
        }
    }

#if DEBUG
    /// <summary>
    /// Reads an AniList token from the launch intent and seeds it into SecureStorage
    /// so the CI emulator can capture authenticated screenshots. The CI workflow
    /// passes the token via: <c>am start --es ci_auth_token "TOKEN"</c>.
    /// Compiled out of Release builds entirely.
    /// </summary>
    private void SeedCiAuthToken()
    {
        string? ciToken = Intent?.GetStringExtra("ci_auth_token");
        if (string.IsNullOrEmpty(ciToken))
        {
            return;
        }

        try
        {
            Task.Run(async () =>
            {
                await SecureStorage.Default.SetAsync("anilist_access_token", ciToken);
                await SecureStorage.Default.SetAsync("anilist_access_token_expires_at",
                    DateTimeOffset.UtcNow.AddYears(1).ToString("O"));
            }).GetAwaiter().GetResult();

            Log.Info(nameof(MainActivity), "CI auth token seeded into SecureStorage");
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainActivity), $"CI auth token seeding failed: {ex.Message}");
        }
    }
#endif
}