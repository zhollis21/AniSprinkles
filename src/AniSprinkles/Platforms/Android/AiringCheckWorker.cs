using System.Diagnostics.CodeAnalysis;
using Android.Content;
using AndroidX.Work;
using AniSprinkles.Utilities;
using Bitmap = global::Android.Graphics.Bitmap;

namespace AniSprinkles.Platforms.Android;

/// <summary>
/// WorkManager <see cref="Worker"/> that polls AniList's public AiringSchedule API for
/// episodes that have aired since the last check, and posts local notifications.
/// Fully self-contained — makes its own HTTP requests without depending on MAUI DI,
/// so it works even if the app hasn't been launched since a device reboot.
/// <para>
/// What is left here is only what needs Android: the WorkManager shell, the shared
/// <see cref="HttpClient"/>, posting notifications, and reading the title-language preference. The
/// check's logic lives in <see cref="AiringCheckRunner"/> and its query in
/// <see cref="AiringScheduleFetcher"/>, both in Core where the test suite can reach them (#141).
/// The delegate handoff is what preserves the no-DI property above.
/// </para>
/// </summary>
public class AiringCheckWorker : Worker
{
    // Shared across all worker runs in this process. HttpClient is thread-safe and designed
    // to be reused. Timeout guards against hung requests stalling the worker thread indefinitely.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(AiringCheckWorker))]
    public AiringCheckWorker(Context context, WorkerParameters workerParams)
        : base(context, workerParams)
    {
    }

    public override Result DoWork()
    {
        try
        {
            var outcome = AiringCheckRunner.Run(
                Preferences.Default,
                TimeProvider.System,
                FetchAiringSchedule,
                PostNotification,
                // WorkManager cancellation (sign-out calls CancelUniqueWork) does not interrupt a
                // run already under way — it only sets this flag — so the runner polls it to avoid
                // posting the previous user's notifications and rewriting state that
                // ClearNotificationState just removed.
                () => IsStopped);

            global::Android.Util.Log.Info(
                "AiringCheckWorker",
                $"DoWork {outcome.Status}: examined {outcome.Examined}, notified {outcome.Notified}");

            return Result.InvokeSuccess()!;
        }
        catch (Exception ex)
        {
            // Don't retry on transient errors; the next periodic run will try again. The runner
            // leaves the checkpoint unadvanced when the fetch throws, so the window is retried
            // rather than silently skipped.
            // Uses Android.Util.Log directly instead of ILogger because WorkManager can
            // instantiate this worker post-reboot before the MAUI DI container is built.
            // The AndroidLogcatLoggerProvider bridges the rest of the app's ILogger output
            // to the same logcat stream, so a single tag filter still captures everything.
            global::Android.Util.Log.Error("AiringCheckWorker", $"DoWork failed: {ex}");
            return Result.InvokeSuccess()!;
        }
    }

    /// <summary>
    /// Reads the title-language preference straight from <c>Preferences.Default</c> — this worker can
    /// run before the app has ever been launched, so <c>AppSettings.Load()</c> may not have happened
    /// and its storage seam is internal to Core.
    /// </summary>
    private static AiringScheduleResult FetchAiringSchedule(
        IReadOnlyList<int> mediaIds, long airingAfter, long airingBefore)
    {
        string langPref = Preferences.Default.Get(
            AppSettings.TitleLanguageKey, nameof(UserTitleLanguage.Romaji));
        _ = Enum.TryParse<UserTitleLanguage>(langPref, out var language);

        return AiringScheduleFetcher.Fetch(HttpClient, mediaIds, airingAfter, airingBefore, language);
    }

    /// <summary>
    /// Downloads the cover art, if any, and posts one notification. The bitmap is disposed here
    /// rather than held — a run can post many, and they are only needed for the Notify call.
    /// </summary>
    private void PostNotification(AiringEntry entry)
    {
        Bitmap? coverBitmap = null;
        if (!string.IsNullOrEmpty(entry.CoverImageUrl))
        {
            coverBitmap = NotificationHelper.DownloadBitmap(entry.CoverImageUrl);
        }

        NotificationHelper.Show(ApplicationContext!, entry.MediaId, entry.MediaTitle, entry.Episode, coverBitmap);
        coverBitmap?.Dispose();
    }
}
