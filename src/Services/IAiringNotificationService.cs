namespace AniSprinkles.Services;

public interface IAiringNotificationService
{
    /// <summary>
    /// Requests the POST_NOTIFICATIONS runtime permission (Android 13+).
    /// Returns true if granted or if the platform does not require it.
    /// </summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// Registers a periodic WorkManager job that polls for newly aired episodes.
    /// Safe to call multiple times — uses <c>ExistingPeriodicWorkPolicy.Keep</c>.
    /// </summary>
    void SchedulePeriodicCheck();

    /// <summary>Cancels the periodic airing check WorkManager job.</summary>
    void CancelPeriodicCheck();

    /// <summary>
    /// Clears cached media IDs, last-check timestamp, and notified-episode set.
    /// Call on sign-out to prevent stale notifications for a different user.
    /// </summary>
    void ClearNotificationState();
}
