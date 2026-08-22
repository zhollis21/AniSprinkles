#if CI
namespace AniSprinkles.Services;

/// <summary>
/// CI-only stub that does nothing — no WorkManager, no notifications, no permissions.
/// Compiled out of Debug and Release builds entirely — only active when -p:CiBuild=true.
/// </summary>
internal sealed class CIAiringNotificationService : IAiringNotificationService
{
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
    public void SchedulePeriodicCheck() { }
    public void CancelPeriodicCheck() { }
    public void ClearNotificationState() { }
}
#endif
