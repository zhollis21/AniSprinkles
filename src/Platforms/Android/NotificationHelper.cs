using Android.App;
using Android.Content;
using Android.Graphics;
using AndroidX.Core.App;

namespace AniSprinkles.Platforms.Android;

/// <summary>
/// Static helper for creating the airing-alerts notification channel and posting notifications.
/// </summary>
public static class NotificationHelper
{
    public const string ChannelId = "airing_alerts";
    private const string GroupKey = "airing_group";

    /// <summary>
    /// Creates the airing alerts notification channel. Safe to call multiple times —
    /// <see cref="NotificationManager.CreateNotificationChannel"/> is idempotent and
    /// will not reset user-modified settings on subsequent calls.
    /// </summary>
    public static void CreateChannel(Context context)
    {
        var channel = new NotificationChannel(ChannelId, "Airing Alerts", NotificationImportance.Default)
        {
            Description = "Notifications when tracked anime episodes air"
        };

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.CreateNotificationChannel(channel);
    }

    /// <summary>
    /// Posts a local notification for a newly aired episode.
    /// </summary>
    public static void Show(Context context, int mediaId, string title, int episode, Bitmap? coverImage)
    {
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
        var pendingIntent = intent is not null
            ? PendingIntent.GetActivity(context, mediaId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
            : null;

        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetSmallIcon(_Microsoft.Android.Resource.Designer.ResourceConstant.Mipmap.appicon);
        builder.SetContentTitle($"{title} \u00b7 Ep {episode}");
        builder.SetContentText("New episode aired");
        builder.SetAutoCancel(true);
        builder.SetGroup(GroupKey);

        if (coverImage is not null)
        {
            builder.SetLargeIcon(coverImage);
        }

        if (pendingIntent is not null)
        {
            builder.SetContentIntent(pendingIntent);
        }

        // Unique notification ID from mediaId + episode to prevent duplicates
        int notificationId = HashCode.Combine(mediaId, episode);
        NotificationManagerCompat.From(context)?.Notify(notificationId, builder.Build());
    }

    /// <summary>
    /// Dismisses all posted airing notifications from the notification shade.
    /// </summary>
    public static void CancelAll(Context context)
    {
        NotificationManagerCompat.From(context)?.CancelAll();
    }

    // Shared client for cover image downloads. Reused across notification posts in the same
    // worker run. Timeout prevents a hung image download from stalling the worker thread.
    private static readonly HttpClient ImageHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Downloads a cover image as a <see cref="Bitmap"/> for use as a notification large icon.
    /// Returns null on failure — the notification should be posted without the image.
    /// </summary>
    public static Bitmap? DownloadBitmap(string url)
    {
        try
        {
            using var stream = ImageHttpClient.GetStreamAsync(url).GetAwaiter().GetResult();
            return BitmapFactory.DecodeStream(stream);
        }
        catch
        {
            return null;
        }
    }
}
