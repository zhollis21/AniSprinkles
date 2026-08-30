#if DEBUG
using Android.App;
using Android.Content;
using AndroidX.Work;
using AniSprinkles.Platforms.Android;

namespace AniSprinkles;

/// <summary>
/// Posts one airing notification on demand over adb, so the notification itself — and the deep link
/// a tap on it follows (#111) — can be exercised on device without waiting for the worker.
/// <para>
/// This exists because that path was otherwise unreachable from a scriptable run, and it is where a
/// real bug lived: the <c>PendingIntent</c>'s request code was per-media, and since extras are not
/// part of a PendingIntent's identity, two episodes of one show shared one — so tapping the older
/// notification consumed the newer one's replay nonce and the newer tap silently did nothing. No
/// unit test can see that; only a real notification can.
/// </para>
/// <para>
/// Deliberately calls <see cref="NotificationHelper.Show"/> directly rather than going through
/// <c>IAiringNotificationService</c>. That keeps <c>CIAiringNotificationService</c> a no-op and
/// leaves the screenshot job untouched — no POST_NOTIFICATIONS dialog over a screenshot, no
/// WorkManager job, and no AniList traffic against the rate-limit budget. Nothing broadcasts to
/// this during CI, so CI behaviour is unchanged by its existence.
/// </para>
/// <example>
/// <code>
/// adb shell am broadcast -n com.RainbowSprinkles.AniSprinkles/.NotifyReceiver \
///   -a com.RainbowSprinkles.NOTIFY --ei mediaId 21 --ei episode 1050 --es title "ONE PIECE"
/// </code>
/// </example>
/// <para>
/// Exported for the same reason, and with the same justification, as
/// <c>FaultBroadcastReceiver</c>: <c>am broadcast</c> cannot reach it otherwise, a Debug build is
/// already debuggable, and the whole type is behind <c>#if DEBUG</c> so it cannot exist in Release.
/// </para>
/// </summary>
[BroadcastReceiver(
    Name = "com.RainbowSprinkles.AniSprinkles.NotifyReceiver",
    Enabled = true,
    Exported = true)]
[IntentFilter([NotifyBroadcastReceiver.NotifyAction])]
public sealed class NotifyBroadcastReceiver : BroadcastReceiver
{
    public const string NotifyAction = "com.RainbowSprinkles.NOTIFY";

    private const int DefaultMediaId = 21;
    private const int DefaultEpisode = 1050;
    private const string DefaultTitle = "ONE PIECE";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        if (intent.GetBooleanExtra("run", false))
        {
            RunWorkerOnce(context, intent);
            return;
        }

        int mediaId = intent.GetIntExtra("mediaId", DefaultMediaId);
        int episode = intent.GetIntExtra("episode", DefaultEpisode);
        string title = intent.GetStringExtra("title") ?? DefaultTitle;

        try
        {
            // No cover art: downloading one would put a real network call on a debugging
            // convenience, and the large icon is not what this is here to prove.
            NotificationHelper.CreateChannel(context);
            NotificationHelper.Show(context, mediaId, title, episode, coverImage: null);

            // Android.Util.Log rather than ILogger, for the same reason FaultBroadcastReceiver does
            // it: a receiver can run before the MAUI container is guaranteed built.
            Android.Util.Log.Info(
                "AniSprinkles", $"NOTIFY posted media={mediaId} episode={episode} title={title}");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("AniSprinkles", $"NOTIFY broadcast failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Enqueues one immediate <see cref="AiringCheckWorker"/> run, so the real
    /// <c>DoWork</c> → <see cref="AiringCheckRunner"/> → <see cref="AiringScheduleFetcher"/> →
    /// notification path executes. That wiring is otherwise unverifiable: a CI build stubs the
    /// service so the worker is never scheduled, and no unit test can reach the delegate handoff.
    /// <para>
    /// Optionally seeds the state the run needs, so one command is a complete test: without cached
    /// media IDs the runner correctly does nothing at all.
    /// </para>
    /// <para>
    /// <b>This makes a real, unauthenticated AniList request</b> — one AiringSchedule query per
    /// invocation, plus one per extra page. Deliberate and manual; nothing broadcasts here in CI, so
    /// it never runs unattended and never costs the rate-limit budget on its own.
    /// </para>
    /// </summary>
    private static void RunWorkerOnce(Context context, Intent intent)
    {
        try
        {
            string? mediaIds = intent.GetStringExtra("mediaIds");
            if (!string.IsNullOrWhiteSpace(mediaIds))
            {
                var ids = mediaIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => int.TryParse(part.Trim(), out int id) ? id : 0)
                    .Where(id => id > 0)
                    .ToList();

                AiringNotificationState.WriteMediaIds(Preferences.Default, ids);
                Android.Util.Log.Info("AniSprinkles", $"NOTIFY seeded media ids: {string.Join(",", ids)}");
            }

            int lookbackHours = intent.GetIntExtra("lookbackHours", 0);
            if (lookbackHours > 0)
            {
                // Backdate the checkpoint so the run has a window wide enough to find something.
                long since = DateTimeOffset.UtcNow.AddHours(-lookbackHours).ToUnixTimeSeconds();
                AiringNotificationState.AdvanceCheckpoint(Preferences.Default, since);
                Android.Util.Log.Info("AniSprinkles", $"NOTIFY checkpoint backdated {lookbackHours}h");
            }

            var request = new OneTimeWorkRequest.Builder(typeof(AiringCheckWorker)).Build();
            WorkManager.GetInstance(context).Enqueue(request);

            Android.Util.Log.Info("AniSprinkles", "NOTIFY enqueued a one-shot AiringCheckWorker run");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("AniSprinkles", $"NOTIFY worker run failed: {ex}");
        }
    }
}
#endif
