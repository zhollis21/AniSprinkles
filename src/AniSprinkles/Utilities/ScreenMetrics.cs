namespace AniSprinkles.Utilities;

/// <summary>
/// The app window's size, for layouts that size themselves to the screen. Use this instead of MAUI's
/// display API, which the app must not read (#230, pinned by <c>DeviceDisplayGuardTests</c>, which is
/// also why this comment doesn't name it).
/// </summary>
/// <remarks>
/// On Android, MAUI 11.0.10 reads the display through the window-manager service and disposes the
/// managed peer it gets back, which Java.Interop shares between callers. Sentry.Maui reads it the same
/// way while building every event, on whatever thread captured it. A read of ours racing one of those
/// can leave a thread holding a deleted JNI reference and abort the process. This reads the activity's
/// display metrics instead, which never touches that service. It also describes the app's own window,
/// which is what a sheet or popup should fit, rather than the whole physical screen.
/// </remarks>
internal static class ScreenMetrics
{
    /// <summary>Width and height in device-independent units, and pixels per unit.</summary>
    public readonly record struct Size(double WidthDip, double HeightDip, double Density);

    public static Size Read()
    {
#if ANDROID
        // Not disposed on purpose: Resources.DisplayMetrics is a shared instance, and disposing a shared
        // peer is exactly the bug this class exists to avoid. The application context covers a call
        // made before an activity exists.
        Android.Content.Context context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var metrics = context.Resources?.DisplayMetrics;
        if (metrics is not null && metrics.Density > 0)
        {
            return new Size(metrics.WidthPixels / metrics.Density, metrics.HeightPixels / metrics.Density, metrics.Density);
        }
#endif
        // What MAUI's display API reported when it could not read the display either.
        return new Size(0, 0, 1);
    }
}
