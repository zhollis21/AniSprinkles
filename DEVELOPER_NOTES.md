# Developer Notes

## Simulating AniList Being Down

The app has classified error handling (`AniListApiException` with `ApiErrorKind`) that shows
different error views for outages, network failures, and auth errors. Here's how to trigger
each scenario locally.

### Option 1: `ErrorSim` build flag (recommended)

The stubs are already wired up. Pass `-p:ErrorSim=true` at build time (Debug only):

```
dotnet build -p:ErrorSim=true
```

Or in Visual Studio: **Project Properties → Build → Conditional compilation symbols → add `ERROR_SIM`**.

This swaps in two pre-built stubs automatically:

- `SimAuthService` (`src/Services/ErrorSimulation/SimAuthService.cs`) — always reports the app as authenticated, no real sign-in needed
- `FailingAniListClient` (`src/Services/ErrorSimulation/FailingAniListClient.cs`) — throws a classified `AniListApiException` on every data call

To change which error kind is simulated, open `src/Services/ErrorSimulation/FailingAniListClient.cs` and
change the `SimulatedError` constant, then rebuild:

```csharp
//   ApiErrorKind.ServiceOutage  → "AniList is Temporarily Down"  (CloudDismiss24)
//   ApiErrorKind.Network        → "No Internet Connection"        (WifiOff24)
//   ApiErrorKind.Authentication → "Session Expired"               (LockClosed24)
//   ApiErrorKind.Unknown        → "Something Went Wrong"          (ErrorCircle24)
private const ApiErrorKind SimulatedError = ApiErrorKind.ServiceOutage;
```

### Option 2: Block the endpoint at the network level

Turn on airplane mode on your device/emulator, or block the API host:

- **Android emulator**: `adb shell settings put global airplane_mode_on 1`
- **Windows/Mac**: Add `127.0.0.1 graphql.anilist.co` to your hosts file
- **Charles/Fiddler proxy**: Map `graphql.anilist.co` to return HTTP 503

This triggers `ApiErrorKind.Network` (connection failure) or `ApiErrorKind.ServiceOutage`
(if the proxy returns 503).

### Option 3: Inject a delay + failure in SendAsync

For a more surgical test, add a temporary `#if DEBUG` block at the top of
`AniListClient.SendAsync`:

```csharp
#if DEBUG
// Simulate outage — remove before committing
await Task.Delay(800, cancellationToken); // realistic latency
throw new AniListApiException(ApiErrorKind.ServiceOutage,
    "AniList API has been temporarily disabled due to stability issues.");
#endif
```

### What to verify

When simulating errors, check each page:

| Page              | Expected behavior                                                                                           |
| ----------------- | ----------------------------------------------------------------------------------------------------------- |
| **My Anime**      | Full-page `ErrorStateView` with retry button. Stale cached data shows instead if a previous load succeeded. |
| **Media Details** | Full-page `ErrorStateView` with retry button. Spinner should NOT be visible alongside the error.            |
| **Settings**      | Full-page `ErrorStateView` with retry button. Login prompt should NOT overlap with error view.              |

For each page, also verify:

- Tapping **Try Again** clears the error and re-attempts the load
- **Show technical details** expands to show the API message (not a stack trace)
- **Copy** and **Share** buttons in the details section work
- The correct icon appears per error kind:

| `ApiErrorKind`   | Icon             | Title                         |
| ---------------- | ---------------- | ----------------------------- |
| `ServiceOutage`  | `CloudDismiss24` | "AniList is Temporarily Down" |
| `Network`        | `WifiOff24`      | "No Internet Connection"      |
| `Authentication` | `LockClosed24`   | "Session Expired"             |
| `Unknown`        | `ErrorCircle24`  | "Something Went Wrong"        |

## AI Tooling Setup

These are personal/machine-level installs — not repo requirements. Each contributor sets these up independently.

### dotnet-maui Plugin (VS Code)

Provides on-demand MAUI skills (Shell navigation, CollectionView, data binding, DI, theming, lifecycle, safe area, environment diagnostics) maintained by the Microsoft dotnet team.

Add to VS Code `settings.json` (`Ctrl+Shift+P` → **Open User Settings JSON**):

```json
"chat.plugins.enabled": true,
"chat.plugins.marketplaces": ["dotnet/skills"]
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
