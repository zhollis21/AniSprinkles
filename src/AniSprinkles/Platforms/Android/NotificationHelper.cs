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

    /// <summary>Media id to open when the notification is tapped (#111). Absent or 0 means no deep link.</summary>
    public const string MediaIdExtra = "anisprinkles.deeplink.mediaId";

    /// <summary>
    /// Identifies one tap, so a re-delivered intent isn't followed twice. The notification id serves:
    /// it is per (media, episode) and, since #141, deterministic across processes.
    /// </summary>
    public const string NonceExtra = "anisprinkles.deeplink.nonce";

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
        // Unique notification ID from mediaId + episode, computed up front because it doubles as the
        // deep link's replay nonce.
        int notificationId = AiringNotificationState.NotificationId(mediaId, episode);

        // An explicit intent rather than the package's launcher intent (#111). The launcher intent
        // is ACTION_MAIN/CATEGORY_LAUNCHER, which just brings the task forward without delivering
        // anything — which is why a tap used to land wherever the user had left the app.
        //
        // ClearTop matters because auth runs in a Chrome Custom Tab, which sits in this task: with
        // SingleTop alone the tab could stay on top. SingleTop (both here and as the activity's
        // launchMode) is what routes this to OnNewIntent instead of recreating the activity.
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        intent.PutExtra(MediaIdExtra, mediaId);
        intent.PutExtra(NonceExtra, notificationId);

        // Request code is the notification id, not the media id. Extras are not part of a
        // PendingIntent's identity — only request code, action, data and component are — so a
        // per-media request code would make two episodes of one show share a single PendingIntent,
        // and UpdateCurrent would overwrite its nonce with whichever episode was posted last.
        // Tapping the older notification would then consume that nonce and the newer one would be
        // rejected as a replay, doing nothing. Per-notification request codes keep them distinct.
        var pendingIntent = PendingIntent.GetActivity(
            context, notificationId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetSmallIcon(_Microsoft.Android.Resource.Designer.ResourceConstant.Mipmap.appicon);
        builder.SetContentTitle(title);
        builder.SetContentText($"Episode {episode} is now available");
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
