---
name: airing-notifications
description: "Airing notification subsystem for AniSprinkles: WorkManager worker, Preferences keys, key files, and design decisions. Use when working on airing notifications, AiringCheckWorker, AiringNotificationService, SettingsPageModel notification toggle, or MyAnimePageModel media ID caching."
---

# Airing Notifications

Background system that polls AniList's public AiringSchedule API and posts local Android notifications when tracked episodes air.

## Architecture

```
SettingsPageModel (toggle on/off)
  → IAiringNotificationService.SchedulePeriodicCheck / CancelPeriodicCheck
    → Android WorkManager PeriodicWorkRequest (every 15 min, network required)
      → AiringCheckWorker.DoWork()  [self-contained, no MAUI DI dependency]
        → Read cached RELEASING media IDs from Preferences
        → Query public AiringSchedule API (no auth token)
        → Filter against notified-set → post local notifications with cover art
```

## Key Files

| File                                              | Role                                                             |
| ------------------------------------------------- | ---------------------------------------------------------------- |
| `src/Services/IAiringNotificationService.cs`      | Interface: permission, schedule, cancel, clear                   |
| `src/Services/AiringNotificationService.cs`       | Android impl: WorkManager + MAUI Permissions                     |
| `src/Platforms/Android/AiringCheckWorker.cs`      | Self-contained Worker: own HTTP client + DTOs, no DI             |
| `src/Platforms/Android/NotificationHelper.cs`     | Channel creation, notification posting, cover image download     |
| `src/Platforms/Android/NotificationPermission.cs` | Custom `BasePlatformPermission` for POST_NOTIFICATIONS (API 33+) |
| `src/Services/CI/CIAiringNotificationService.cs`  | CI stub (all no-ops)                                             |

## Preferences Keys

| Key                 | Type     | Purpose                                                             |
| ------------------- | -------- | ------------------------------------------------------------------- |
| `airing_media_ids`  | `string` | Comma-separated RELEASING media IDs (written by `MyAnimePageModel`) |
| `airing_last_check` | `long`   | Unix timestamp of last Worker run                                   |
| `airing_notified`   | `string` | JSON dict of `"mediaId:episode": timestamp` pairs                   |

## Design Decisions

- Worker is **fully self-contained** (own HttpClient, own DTOs) so it works after device reboot without app launch.
- `[DynamicDependency]` on Worker constructor (not class) for Release trimming/AOT safety.
- `_suppressNotificationToggle` flag in `SettingsPageModel` prevents side effects when populating toggle from server state. Code that reverts the toggle under this flag **must** call `TriggerAutoSave()` explicitly afterward — the flag bypasses `OnAiringNotificationsChanged` and its normal autosave path, so without it the reverted value is never persisted to AniList.
- `MyAnimePageModel` caches RELEASING media IDs (Watching + Rewatching + Planning) to Preferences after list load.
- Notified-set entries pruned after 7 days; pruning runs unconditionally (not gated on new entries).
- Sign-out clears all notification state AND dismisses posted notifications from the shade.
- Two distinct denial paths exist: (1) `HandleAiringNotificationToggleAsync` (user taps toggle) — stays on UI thread, no `ConfigureAwait(false)`. (2) `EnsureNotificationPermissionAndScheduleAsync` (called from `PopulateFromUser`) — `RequestPermissionAsync` uses `ConfigureAwait(false)` so continuation may be on a pool thread; the toggle revert and `TriggerAutoSave()` are dispatched to the UI thread via `_dispatcher.Dispatch`.
