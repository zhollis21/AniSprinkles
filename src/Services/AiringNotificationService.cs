using AndroidX.Work;
using AniSprinkles.Platforms.Android;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// Android implementation of <see cref="IAiringNotificationService"/> that uses WorkManager
/// to schedule periodic airing checks and MAUI Permissions for the POST_NOTIFICATIONS runtime request.
/// </summary>
public class AiringNotificationService(ILogger<AiringNotificationService> logger) : IAiringNotificationService
{
    private const string UniqueWorkName = "airing_check";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    public async Task<bool> RequestPermissionAsync()
    {
        // On Android < 13 (API 32 and below) POST_NOTIFICATIONS is not required
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return true;
        }

        var status = await Permissions.RequestAsync<NotificationPermission>().ConfigureAwait(false);
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        logger.LogWarning("POST_NOTIFICATIONS permission denied (status: {Status})", status);
        return false;
    }

    public void SchedulePeriodicCheck()
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)
            .Build();

        var workRequest = new PeriodicWorkRequest.Builder(
                typeof(AiringCheckWorker), PollInterval)
            .SetConstraints(constraints)
            .Build();

        WorkManager.GetInstance(Platform.AppContext)
            .EnqueueUniquePeriodicWork(UniqueWorkName, ExistingPeriodicWorkPolicy.Keep!, workRequest);

        logger.LogInformation("Airing check WorkManager job scheduled (interval: {Interval})", PollInterval);
    }

    public void CancelPeriodicCheck()
    {
        WorkManager.GetInstance(Platform.AppContext).CancelUniqueWork(UniqueWorkName);
        logger.LogInformation("Airing check WorkManager job cancelled");
    }

    public void ClearNotificationState()
    {
        Preferences.Default.Remove("airing_media_ids");
        Preferences.Default.Remove("airing_last_check");
        Preferences.Default.Remove("airing_notified");
        NotificationHelper.CancelAll(Platform.AppContext);
        logger.LogInformation("Airing notification state cleared");
    }
}
