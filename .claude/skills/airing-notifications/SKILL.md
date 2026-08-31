---
name: airing-notifications
description: "Airing notification subsystem for AniSprinkles: WorkManager worker, Preferences keys, key files, and design decisions. Use when working on airing notifications, AiringCheckWorker, AiringNotificationService, SettingsPageModel notification toggle, or AnimeLibraryPageModel media ID caching."
---

# Airing Notifications

Background system that polls AniList's public AiringSchedule API and posts local Android notifications when tracked episodes air.

## Architecture

```
SettingsPageModel (toggle on/off)
  → IAiringNotificationService.SchedulePeriodicCheck / CancelPeriodicCheck
    → Android WorkManager PeriodicWorkRequest (every 15 min, network required)
      → AiringCheckWorker.DoWork()  [self-contained, no MAUI DI dependency]
        → AiringCheckRunner.Run(prefs, clock, fetch, notify, isCancelled)  [Core, testable]
            → Read cached RELEASING media IDs from Preferences
            → fetch  → worker queries public AiringSchedule API (no auth token)
            → Filter against notified-set
            → notify → worker posts local notifications with cover art
            → Advance checkpoint, prune notified-set
```

The split is the point: everything platform-bound stays in the worker, and the logic worth testing
lives in Core behind delegates (#141). The worker still constructs no services and touches no
container, which is what lets it run post-reboot before the app has launched.

## Key Files

| File                                              | Role                                                             |
| ------------------------------------------------- | ---------------------------------------------------------------- |
|  `src/AniSprinkles.Core/Services/IAiringNotificationService.cs` | Interface: permission, schedule, cancel, clear                   |
| `src/AniSprinkles.Core/Services/AiringNotificationState.cs`    | **All four preference keys**, plus parsing, prune, dedup key, notification id |
| `src/AniSprinkles.Core/Services/AiringCheckRunner.cs`          | The check's control flow: window, dedup, checkpoint, cancellation |
| `src/AniSprinkles.Core/Utilities/TitleSelector.cs`             | The one title-language fallback chain, shared with `DisplayTitle` |
| `src/AniSprinkles.Core/Utilities/PendingDeepLink.cs`           | Notification tap → route, and when it is safe to follow (#111)   |
| `src/AniSprinkles/Services/AiringNotificationService.cs`       | Android impl: WorkManager + MAUI Permissions                     |
| `src/AniSprinkles/Platforms/Android/AiringCheckWorker.cs`      | Self-contained Worker: own HTTP client + DTOs, no DI             |
| `src/AniSprinkles/Platforms/Android/NotificationHelper.cs`     | Channel creation, notification posting, cover image download     |
| `src/AniSprinkles/Platforms/Android/NotificationPermission.cs` | Custom `BasePlatformPermission` for POST_NOTIFICATIONS (API 33+) |
| `src/AniSprinkles/Services/CI/CIAiringNotificationService.cs`  | CI stub (all no-ops)                                             |

## Preferences Keys

**Never spell these as literals.** Every one is a const on `AiringNotificationState`, and reads and
writes go through its methods. They used to be independent literals across both projects — the
writer in Core, the reader in the app — so renaming either half left the code compiling, the tests
green, and notifications silently dead (#141).

| Key                          | Const                                    | Type     | Purpose                                                             |
| ---------------------------- | ---------------------------------------- | -------- | ------------------------------------------------------------------- |
| `airing_media_ids`           | `AiringNotificationState.MediaIdsKey`    | `string` | Comma-separated RELEASING media IDs (written by `AnimeLibraryPageModel`) |
| `airing_last_check`          | `AiringNotificationState.LastCheckKey`   | `long`   | Unix timestamp of last successful Worker run                        |
| `airing_notified`            | `AiringNotificationState.NotifiedKey`    | `string` | JSON dict of `"mediaId:episode": timestamp` pairs                   |
| `airing_permission_prompted` | `AiringNotificationState.PermissionPromptedKey` | `bool` | Whether the Library permission prompt has been shown         |
| `title_language`             | `AppSettings.TitleLanguageKey`           | `string` | Read by the Worker for notification titles; owned by `AppSettings`  |

## Design Decisions

- Worker is **fully self-contained** (own HttpClient, own DTOs) so it works after device reboot without app launch.
- `[DynamicDependency]` on Worker constructor (not class) for Release trimming/AOT safety.
- `_suppressNotificationToggle` flag in `SettingsPageModel` prevents side effects when populating toggle from server state. Code that reverts the toggle under this flag **must** call `TriggerAutoSave()` explicitly afterward — the flag bypasses `OnAiringNotificationsChanged` and its normal autosave path, so without it the reverted value is never persisted to AniList.
- `AnimeLibraryPageModel` caches RELEASING media IDs (Watching + Rewatching + Planning) to Preferences after list load. The manga half overrides that hook to nothing — manga does not air.
- Notified-set entries pruned after 7 days; the prune runs unconditionally (only the *write* is gated, on new entries or a non-empty prune). The cutoff is inclusive — an entry exactly at it survives.
- **The checkpoint advances only after a fetch that returned.** `fetch` must throw on any failure, including an HTTP 200 carrying a GraphQL `errors` array; returning empty would silently mark a failed window as checked. The window's end is captured *before* the fetch and reused as the new checkpoint, so the request's own duration can't fall outside every window.
- Nothing bounds how wide that window can grow — three paths leave the checkpoint unadvanced indefinitely. Tracked as #144; `AiringCheckRunner` deliberately preserves the current arithmetic.
- **Cancelling mid-run persists nothing at all.** `WorkManager.CancelUniqueWork` does not interrupt a run already under way, so the runner polls `isCancelled` (the worker passes `IsStopped`) before each notification and again before the writes. Without it, sign-out could be followed by the previous user's notifications and a rewrite of the keys it had just cleared.
- Notification ids come from `AiringNotificationState.NotificationId`, a deterministic FNV-1a over the dedup key. Not `HashCode.Combine` — that is seeded randomly per process, so ids changed on every restart and Android could not update a posted notification in place.
- Sign-out clears all notification state AND dismisses posted notifications from the shade.
- **Tapping a notification deep-links to `media-details` (#111).** The `PendingIntent` is an explicit intent to `MainActivity` carrying a `mediaId` extra — not a URI scheme, so nothing outside the app can invoke it — with `NewTask | ClearTop | SingleTop`. `ClearTop` is there because auth runs in a Chrome Custom Tab that sits in the same task.
- The tap can arrive in four states, and they don't split cleanly into cold/warm — the process may be alive with only the activity destroyed, in which case Shell may *already* exist. So the drain is attempt-based, not lifecycle-branched: tried on intent arrival, on `AppShell`'s `Navigated`, and on `OnResume`. `PendingDeepLink` clears only once navigation is actually attempted, because `MauiShellNavigationService.GoToAsync` returns a completed task when `Shell.Current` is null and so cannot report a no-op.
- Repeat taps **push**, so three notifications give three stacked details pages and back walks them in reverse.
- **Replay is guarded twice, and the second guard has to be persistent.** Android records the intent that *created* a task and replays it whenever it rebuilds that task after killing the process. Verified on device: `am kill` then restore from recents gave `OnCreate(savedInstanceState=present)` in a new PID carrying the original extras intact, several taps later. `RemoveExtra` on our own `Intent` cannot reach the system's copy, and an in-memory guard dies with the process — so the followed nonce is stored under `deeplink_consumed_nonce`. A restore then lands on the Library tab rather than re-opening a possibly week-old notification's anime.
- That key is deliberately **not** in `ClearAll`: clearing it on sign-out would re-enable the replay it exists to prevent, and it is one integer that says nothing about which account is signed in.
- The PendingIntent's **request code is the notification id, not the media id**. Extras are not part of a PendingIntent's identity (only request code, action, data, component), so a per-media request code makes two episodes of one show share a PendingIntent — `UpdateCurrent` then overwrites its nonce with the newer episode's, and tapping the older notification consumes it so the newer one is rejected as a replay and does nothing.
- Two distinct denial paths exist: (1) `HandleAiringNotificationToggleAsync` (user taps toggle) — stays on UI thread, no `ConfigureAwait(false)`. (2) `EnsureNotificationPermissionAndScheduleAsync` (called from `PopulateFromUser`) — `RequestPermissionAsync` uses `ConfigureAwait(false)` so continuation may be on a pool thread; the toggle revert and `TriggerAutoSave()` are dispatched to the UI thread via `_dispatcher.Dispatch`.
