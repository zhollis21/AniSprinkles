#if DEBUG
using Android.App;
using Android.Content;
using AniSprinkles.Services.FaultInjection;
using AniSprinkles.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace AniSprinkles;

/// <summary>
/// Arms and clears fault injection over adb, so changing what fails costs three lines of the normal
/// device pass instead of a rebuild (#125).
/// <para>
/// The old <c>FailingAniListClient</c> kept its error kind in a <c>const</c>, so every variation was
/// an edit plus a ~3 minute rebuild — which is exactly why error states did not get reached for.
/// </para>
/// <example>
/// <code>
/// adb shell am broadcast -n com.RainbowSprinkles.AniSprinkles/.FaultReceiver \
///   -a com.RainbowSprinkles.FAULT --es op GetStudio --es kind NotFound --es scope next
/// adb shell am broadcast -n com.RainbowSprinkles.AniSprinkles/.FaultReceiver \
///   -a com.RainbowSprinkles.FAULT --ez clear true
/// </code>
/// </example>
/// <para>
/// Exported, because <c>am broadcast</c> cannot reach it otherwise. Acceptable here and only here: a
/// Debug build is already debuggable, so anyone with adb access can <c>run-as</c> the package
/// regardless, and the whole type is behind <c>#if DEBUG</c> so it cannot exist in a Release build.
/// An explicit <c>Name</c> pins the component so adb can target it directly — without it the
/// generated name is a <c>crc64…</c> mangling that nobody can type.
/// </para>
/// </summary>
[BroadcastReceiver(
    Name = "com.RainbowSprinkles.AniSprinkles.FaultReceiver",
    Enabled = true,
    Exported = true)]
[IntentFilter([FaultBroadcastReceiver.FaultAction])]
public sealed class FaultBroadcastReceiver : BroadcastReceiver
{
    public const string FaultAction = "com.RainbowSprinkles.FAULT";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent is null)
        {
            return;
        }

        FaultState state;
        try
        {
            state = ServiceProviderHelper.GetServiceProvider().GetRequiredService<FaultState>();
        }
        catch (Exception ex)
        {
            // A broadcast can cold-start the process ahead of DI being wired. Log and drop rather
            // than take the app down over a debugging convenience — re-arm once it is running.
            Android.Util.Log.Warn("AniSprinkles", $"FAULT broadcast ignored — DI not ready: {ex.Message}");
            return;
        }

        if (intent.GetBooleanExtra("clear", false))
        {
            state.Clear();
            Android.Util.Log.Info("AniSprinkles", "FAULT cleared");
            return;
        }

        var profile = new FaultProfile(
            OperationPrefix: NullIfBlank(intent.GetStringExtra("op")),
            Kind: ParseKind(intent.GetStringExtra("kind")),
            Scope: ParseScope(intent.GetStringExtra("scope"), intent.GetIntExtra("n", 0)),
            Delay: TimeSpan.FromMilliseconds(Math.Max(0, intent.GetIntExtra("delay", 0))),
            Layer: ParseLayer(intent.GetStringExtra("layer")),
            AsGraphQlError: intent.GetBooleanExtra("graphql", false));

        state.Arm(profile);

        // Android.Util.Log rather than ILogger, for the same reason MainActivity and
        // AiringCheckWorker do it: a receiver can run before the MAUI container is guaranteed built.
        // This is the acknowledged exception in AGENTS.md, not a new direct-logging call site.
        Android.Util.Log.Info(
            "AniSprinkles",
            $"FAULT armed op={profile.OperationPrefix ?? "<any>"} kind={profile.Kind?.ToString() ?? "<delay only>"} " +
            $"scope={profile.Scope.Kind}({profile.Scope.N}) delay={profile.Delay.TotalMilliseconds}ms " +
            $"layer={profile.Layer} graphql={profile.AsGraphQlError}");
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Absent or unrecognised means "no failure" — a delay-only profile, which is valid.</summary>
    private static ApiErrorKind? ParseKind(string? value)
        => Enum.TryParse<ApiErrorKind>(value, ignoreCase: true, out var kind) ? kind : null;

    private static FaultLayer ParseLayer(string? value)
        => Enum.TryParse<FaultLayer>(value, ignoreCase: true, out var layer) ? layer : FaultLayer.Client;

    /// <summary>
    /// Accepts <c>next</c>, <c>always</c>, <c>everynth</c>, <c>firstn</c>, with the count either
    /// inline (<c>firstn:2</c>) or as a separate <c>--ei n 2</c>. Both spellings exist because the
    /// inline form keeps a one-liner short while the extra is easier to script.
    /// </summary>
    private static FaultScope ParseScope(string? value, int nExtra)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FaultScope.Next;
        }

        var parts = value.Split(':', 2);
        var n = nExtra;
        if (parts.Length == 2 && int.TryParse(parts[1], out var inline))
        {
            n = inline;
        }

        return parts[0].Trim().ToLowerInvariant() switch
        {
            "always" => FaultScope.Always,
            "everynth" => FaultScope.EveryNth(n),
            "firstn" => FaultScope.FirstN(n),
            _ => FaultScope.Next,
        };
    }
}
#endif
