using Android.App;
using Android.Content.PM;

namespace AniSprinkles.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // TODO: This doesn't seem to be needed anymore, but I'll leave it as a scomment just in case
    //protected override void OnDestroy()
    //{
    //    // Suppress ObjectDisposedException that occurs when Shell tries to access 
    //    // the service provider after it has been disposed during activity destruction.
    //    // This is a known issue with MAUI on Android where the service provider 
    //    // is disposed before the Shell cleanup is complete.
    //    try
    //    {
    //        base.OnDestroy();
    //    }
    //    catch (ObjectDisposedException ode) when (ode.ObjectName?.Contains("IServiceProvider") == true)
    //    {
    //        // Safe to ignore - the service provider cleanup issue
    //    }
    //    catch (Exception ex) when (ex.GetType().Name == "JavaProxyThrowable" && ex.InnerException is ObjectDisposedException ode && ode.ObjectName?.Contains("IServiceProvider") == true)
    //    {
    //        // Same issue but wrapped in Java exception
    //    }
    //}
}
