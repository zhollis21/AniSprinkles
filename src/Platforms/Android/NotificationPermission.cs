using Android;

namespace AniSprinkles.Platforms.Android;

/// <summary>
/// Custom MAUI permission class for <c>POST_NOTIFICATIONS</c> (required on Android 13+ / API 33).
/// MAUI does not include a built-in permission type for this, so we define one.
/// </summary>
public class NotificationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? [(Manifest.Permission.PostNotifications, true)]
            : [];
}
