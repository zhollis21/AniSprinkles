---
description: "C# code style, MVVM patterns, async rules, and UI-thread safety for AniSprinkles. Use when writing or reviewing C# code, implementing PageModels, services, or converters."
applyTo: "**/*.cs"
---

# C# Patterns

## Code Style

Follow `.editorconfig`. Key rules:

- 4-space indent, CRLF line endings, Allman braces (`csharp_new_line_before_open_brace = all`), file-scoped namespaces, braces always required.
- `var` only when type is apparent; use explicit types for built-in types and unclear types.
- Expression-bodied members for properties/accessors/lambdas; **not** for constructors, methods, or local functions.
- Primary constructors preferred.
- Private fields: `_camelCase`. Constants/static readonly: `PascalCase`. Interfaces: `I`-prefixed.
- Modifier order: `public, private, protected, internal, file, static, extern, new, virtual, abstract, sealed, override, readonly, unsafe, required, volatile, async`.
- Comments explain **why** (intent, tradeoffs, guardrails), not what the code does. Use `///` XML doc on public APIs.

## Nullable

Nullable context is enabled project-wide (`<Nullable>enable</Nullable>`). Use nullable annotations (`string?`, `Media?`) appropriately. Do not suppress nullable warnings without justification.

## Async and Cancellation

- Every async service method takes `CancellationToken cancellationToken = default`.
- Use `ConfigureAwait(false)` on all service-layer awaits.
- **UI-thread safety**: any `[ObservableProperty]` or bound property MUST be set from the UI thread. After `await` with `ConfigureAwait(false)`, the continuation may be on a pool thread. If a method sets bound properties on a failure/revert path, do NOT use `ConfigureAwait(false)` on that path.

## MVVM (CommunityToolkit.Mvvm 8.4)

- PageModels extend `ObservableObject`.
- Use `[ObservableProperty]` for bindable properties; `[RelayCommand]` for commands; `[NotifyPropertyChangedFor]` for dependent properties.
- PageModels use the **two-constructor pattern**: a parameterless constructor (for XAML tooling) and a DI constructor. `ServiceProviderHelper` resolves services when `Application.Current.Handler` isn't ready during Shell startup.

## Logging and Telemetry

- Use `ILogger` with structured messages: `"HTTP {Method} {Uri}"` — not string interpolation.
- Debug file logs guarded with `#if DEBUG`, written via `FileLoggerProvider` (rotating files at `files/logs/anisprinkles.log`). Category filters: `Microsoft`/`System`/`Sentry` → Warning; app code → Information.
- Telemetry via Sentry (`SendDefaultPii = false`, no performance tracing). Use `ErrorReportService.Record()` for handled exceptions. Bearer tokens are always redacted before logging.
- Do not mix WPF/Xamarin.Forms/Blazor/React patterns into MAUI code.
