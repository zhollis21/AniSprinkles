# Developer Notes

## Simulating AniList Being Down

The app has classified error handling (`AniListApiException` with `ApiErrorKind`) that shows
different error views for outages, network failures, and auth errors. Here's how to trigger
each scenario locally.

### Option 1: Runtime fault injection (recommended)

Every Debug build ships with fault injection compiled in and **disarmed**, so there is no special
build and no rebuild between scenarios — you arm faults in the app you are already running (#125).

```powershell
# Break the next GetStudio call, then let it succeed on Retry
pwsh .claude/skills/run-anisprinkles/driver.ps1 fault GetStudio NotFound next

# Every Discover call fails until cleared
pwsh .claude/skills/run-anisprinkles/driver.ps1 fault GetDiscoverSections ServiceOutage always

# 4s of latency on any call, without failing it — opens the cancellation / debounce windows
pwsh .claude/skills/run-anisprinkles/driver.ps1 fault any delay next -delay 4000

pwsh .claude/skills/run-anisprinkles/driver.ps1 fault clear
```

`fault <op> <kind> [scope]`, where `op` is an `IAniListClient` method prefix (`GetStudio` matches
`GetStudioAsync`) or `any`; `kind` is an `ApiErrorKind` or `delay`; and `scope` is
`next` (default), `always`, `firstn:N` or `everynth:N`. All scopes are deterministic — a fault you
saw once can always be re-run.

**It decorates the client rather than replacing it**, which is the important part: it composes with
the CI stubs (`-p:CiBuild=true`), so a real screen loads from the fixtures and then the *next* call
breaks. That is what makes the details pages' error → **Try Again** → content path reachable at all.
The old `ErrorSim` build failed every call, so you could never get to the screen you wanted to break.

Two seams, selected with `-layer`:

| Layer | Injects at | Composes with CI stubs | Exercises |
| --- | --- | --- | --- |
| `client` (default) | `IAniListClient` decorator | Yes | Page-model error/retry states, `ListOperationRunner` snackbars, cancellation |
| `http` | inside the `HttpClient` pipeline | No — needs a real signed-in session | `AniListRateLimitHandler` (incl. `Retry-After`), `SendAsync` retry-once, `AniListErrorClassifier` |

```powershell
# Synthetic 429 with a 2s Retry-After — the rate-limit handler absorbs it and retries
driver.ps1 fault any RateLimited next -layer http -delay 2000

# ...and a 6s one, over maxAutoRetryWait, so it surfaces to the user instead
driver.ps1 fault any RateLimited next -layer http -delay 6000

# HTTP 200 carrying a GraphQL errors array, which AniList genuinely does
driver.ps1 fault any NotFound next -layer http -graphql
```

Nothing here compiles into a Release build — the whole path is behind `#if DEBUG`, which is why the
old `-p:ErrorSim=true` flag and its Release guard are gone.

### Option 2: Block the endpoint at the network level

Turn on airplane mode on your device/emulator, or block the API host:

- **Android emulator**: `adb shell settings put global airplane_mode_on 1`
- **Windows/Mac**: Add `127.0.0.1 graphql.anilist.co` to your hosts file
- **Charles/Fiddler proxy**: Map `graphql.anilist.co` to return HTTP 503

This triggers `ApiErrorKind.Network` (connection failure) or `ApiErrorKind.ServiceOutage`
(if the proxy returns 503).

> There used to be an Option 3 here: hand-edit a temporary `#if DEBUG` delay-and-throw into
> `AniListClient.SendAsync`. Option 1 now does exactly that — with a `Delay`, at either seam, without
> touching shipping code and without a rebuild — so editing the client by hand is no longer worth the
> risk of committing it by accident.

### What to verify

When simulating errors, check each page:

| Page              | Expected behavior                                                                                           |
| ----------------- | ----------------------------------------------------------------------------------------------------------- |
| **My Anime**      | Full-page `ErrorStateView` with retry button. Stale cached data shows instead if a previous load succeeded. |
| **Media Details** | Full-page `ErrorStateView` with retry button. Spinner should NOT be visible alongside the error.            |
| **Settings**      | Full-page `ErrorStateView` with retry button. Login prompt should NOT overlap with error view.              |

**Recovery is the case worth spending time on**, because it is the one that was unreachable before
runtime fault injection: with `scope next` the fault is spent on the first call, so **Try Again**
actually succeeds and the page returns to Content. Drive all four details pages (Media, Staff,
Character, Studio) through error → Try Again → Content — plus a section's **Load More**, which is
where `ListOperationRunner`'s swallow-and-snackbar contract lives:

```powershell
driver.ps1 fault GetMedia ServiceOutage next          # details page error, recovers on Retry
driver.ps1 fault LoadMediaCharactersPage Network next # section Load More fails, page stays loaded
```

For each page, also verify:

- Tapping **Try Again** clears the error and re-attempts the load
- **Show technical details** expands to show the API message (not a stack trace)
- **Copy** and **Share** buttons in the details section work
- The correct icon appears per error kind:

| `ApiErrorKind`   | Icon              | Title                    |
| ---------------- | ----------------- | ------------------------ |
| `ServiceOutage`  | `CloudDismiss24`  | "AniList is Down"        |
| `Network`        | `WifiOff24`       | "No Internet Connection" |
| `Authentication` | `LockClosed24`    | "Session Expired"        |
| `RateLimited`    | `Clock24`         | "Slow Down a Sec"        |
| `NotFound`       | `DismissCircle24` | "Entry Unavailable"      |
| `Unknown`        | `ErrorCircle24`   | "Something Went Wrong"   |

Source of truth is `AniListApiException.UserTitle` / `.IconGlyph`
(`src/AniSprinkles.Core/Services/AniListApiException.cs`) — check there if this table and the app
disagree.

## AI Tooling Setup

These are personal/machine-level installs — not repo requirements. Each contributor sets these up independently.

### dotnet-maui Plugin (VS Code)

Provides on-demand MAUI skills (Shell navigation, CollectionView, data binding, DI, theming, lifecycle, safe area, environment diagnostics) maintained by the Microsoft dotnet team.

Add to VS Code `settings.json` (`Ctrl+Shift+P` → **Open User Settings JSON**):

```json
{
  "chat.plugins.enabled": true,
  "chat.plugins.marketplaces": ["dotnet/skills"]
}
```

Then type `/plugins` in Copilot Chat and install `dotnet-maui`. Invoke skills with e.g. `/maui-shell-navigation`.

> VS Code plugin support is preview — may require enabling in settings first.

### Recommended MCPs

| MCP                     | Purpose                                                                                          |
| ----------------------- | ------------------------------------------------------------------------------------------------ |
| **Context7**            | Live .NET / MAUI / Android docs lookup without leaving the editor                                |
| **Sequential Thinking** | Complex multi-step reasoning tasks                                                               |
| **GitHub MCP**          | Issue and PR context (available via VS Code extension; standalone install helps other harnesses) |

MCP configuration varies by harness (VS Code, Claude Code, Cursor, etc.) — consult your client's MCP setup docs.
