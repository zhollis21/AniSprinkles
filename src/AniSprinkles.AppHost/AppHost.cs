// AppHost for local-dev observability of the AniSprinkles MAUI app.
//
// Registers the MAUI project by path (NOT by ProjectReference — TFM mismatch
// between net10.0 here and net10.0-android over there would break the build)
// and attaches an Android emulator device configuration with an OTLP Dev
// Tunnel so telemetry from the emulator reaches the Aspire dashboard on the
// host.
//
// AniList and Sentry are modeled as external services so they appear in the
// resource graph. Only AniList gets WithReference — Sentry is a node-only
// pointer because the app uses its own hardcoded DSN and doesn't need
// service-discovery env vars.
var builder = DistributedApplication.CreateBuilder(args);

var anilist = builder.AddExternalService("anilist", "https://graphql.anilist.co");

// Sentry appears as a graph node without a reference — the app uses its own
// hardcoded DSN, so service-discovery env vars would be dead weight.
builder.AddExternalService("sentry", "https://sentry.io");

var mauiapp = builder.AddMauiProject(
    name: "anisprinkles",
    projectPath: "../AniSprinkles.csproj");

mauiapp.AddAndroidEmulator()
    .WithOtlpDevTunnel()
    .WithReference(anilist)
    .WithUrl("https://github.com/zhollis21/AniSprinkles", "GitHub repo")
    // Workaround for Aspire.Hosting.Maui 13.2.2-preview.1 bug: ConfigurePlatformResource
    // adds "run" via WithArgs even though DCP's PrepareProjectExecutables already prepends
    // "run" for ProjectResource. Without this removal, dotnet run receives a duplicate
    // "run" arg and Microsoft.Android.Run crashes with "Unexpected argument(s): run".
    // See https://github.com/dotnet/aspire/issues/15248.
    .WithArgs(ctx => ctx.Args.Remove("run"));

builder.Build().Run();
