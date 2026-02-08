using Android.App;
using Android.Content.PM;
using Android.OS;

namespace AniSprinkles
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Handle back navigation through Shell instead of closing the app
            var callback = new ShellBackPressedCallback(this);
            OnBackPressedDispatcher.AddCallback(this, callback);
        }

        private class ShellBackPressedCallback : AndroidX.Activity.OnBackPressedCallback
        {
            private readonly Activity _activity;

            public ShellBackPressedCallback(Activity activity) : base(true)
            {
                _activity = activity;
            }

            public override async void HandleOnBackPressed()
            {
                // Attempt Shell navigation first
                if (Shell.Current != null)
                {
                    try
                    {
                        await Shell.Current.GoToAsync("..");
                    }
                    catch (Exception ex)
                    {
                        // Only fall back to default behavior if Shell navigation fails
                        System.Diagnostics.Debug.WriteLine($"Shell navigation failed: {ex.Message}");
                        _activity.Finish();
                    }
                }
                else
                {
                    // Shell not initialized yet, use default behavior
                    _activity.Finish();
                }
            }
        }

        protected override void OnDestroy()
        {
            // Suppress ObjectDisposedException that occurs when Shell tries to access 
            // the service provider after it has been disposed during activity destruction.
            // This is a known issue with MAUI on Android where the service provider 
            // is disposed before the Shell cleanup is complete.
            try
            {
                base.OnDestroy();
            }
            catch (ObjectDisposedException ode) when (ode.ObjectName?.Contains("IServiceProvider") == true)
            {
                // Safe to ignore - the service provider cleanup issue
            }
            catch (Exception ex) when (ex.GetType().Name == "JavaProxyThrowable" && ex.InnerException is ObjectDisposedException ode && ode.ObjectName?.Contains("IServiceProvider") == true)
            {
                // Same issue but wrapped in Java exception
            }
        }
    }
}
