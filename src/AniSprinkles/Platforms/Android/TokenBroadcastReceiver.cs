#if DEBUG
using Android.App;
using Android.Content;
using AniSprinkles.Services;
using AniSprinkles.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace AniSprinkles;

/// <summary>
/// Signs a Debug build in over adb, and hands the resulting token back out again.
/// <para>
/// The CI stubs are the only scriptable way past sign-in, which means any device pass that needs
/// *real* AniList data has to be driven by hand. That is the gap this closes: seed a token once and
/// a real-auth build comes up signed in, with the driver able to tap through it like any other.
/// </para>
/// <para>
/// The <c>dump</c> direction exists because obtaining a token by hand is genuinely awkward:
/// AniList's implicit grant returns it in the fragment of a redirect to <c>anisprinkles://auth</c>,
/// a custom scheme no desktop browser will follow. The app already performs that flow correctly, so
/// signing in on the emulator and reading the token back out is less friction than registering a
/// second OAuth client just to have a redirect a browser can land on.
/// </para>
/// <example>
/// <code>
/// adb shell am broadcast -n com.RainbowSprinkles.AniSprinkles/.TokenReceiver \
///   -a com.RainbowSprinkles.TOKEN --es token &lt;access-token&gt;
/// adb shell am broadcast -n com.RainbowSprinkles.AniSprinkles/.TokenReceiver \
///   -a com.RainbowSprinkles.TOKEN --ez dump true
/// adb shell am broadcast -n com.RainbowSprinkles.AniSprinkles/.TokenReceiver \
///   -a com.RainbowSprinkles.TOKEN --ez clear true
/// </code>
/// Normally reached through <c>driver.ps1 seed-token [test|real]</c> and
/// <c>driver.ps1 dump-token</c>, which keep the value in the environment or a gitignored file so it
/// never has to be typed or printed.
/// </example>
/// <para>
/// <b>The token does travel on an adb command line</b>, where <c>am</c> may echo it into logcat.
/// That is a real exposure and the reason this is worth stating rather than glossing: it is
/// acceptable only because this is a local Debug build on a throwaway emulator, seeded with a
/// throwaway account's token. It is not a pattern to reach for anywhere near a real credential —
/// and like <see cref="FaultBroadcastReceiver"/>, the whole type is behind <c>#if DEBUG</c> so it
/// cannot exist in a Release build.
/// </para>
/// <para>
/// Exported for the same reason the fault receiver is: <c>am broadcast</c> cannot reach it
/// otherwise, and a Debug build is already debuggable. The explicit <c>Name</c> pins the component
/// so adb can target it without the generated <c>crc64…</c> mangling.
/// </para>
/// </summary>
[BroadcastReceiver(
    Name = "com.RainbowSprinkles.AniSprinkles.TokenReceiver",
    Enabled = true,
    Exported = true)]
[IntentFilter([TokenBroadcastReceiver.TokenAction])]
public sealed class TokenBroadcastReceiver : BroadcastReceiver
{
    public const string TokenAction = "com.RainbowSprinkles.TOKEN";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent is null)
        {
            return;
        }

        TokenStore? store;
        try
        {
            // GetService, not GetRequiredService: a CI-stub build is still a Debug build, so this
            // receiver exists there too — but CIAuthService replaces the real auth stack and no
            // TokenStore is registered. Seeding one would be meaningless rather than fatal, so say
            // so plainly instead of throwing inside a broadcast.
            store = ServiceProviderHelper.GetServiceProvider().GetService<TokenStore>();
        }
        catch (Exception ex)
        {
            // A broadcast can cold-start the process ahead of DI being wired. Log and drop rather
            // than take the app down over a debugging convenience — re-send once it is running.
            Android.Util.Log.Warn("AniSprinkles", $"TOKEN broadcast ignored — DI not ready: {ex.Message}");
            return;
        }

        if (store is null)
        {
            Android.Util.Log.Warn(
                "AniSprinkles",
                "TOKEN broadcast ignored — no TokenStore registered. This is a CI-stub build "
                + "(-p:CiBuild=true), where sign-in is faked by CIAuthService and a real token has "
                + "nothing to do. Rebuild without CiBuild to use a seeded token.");
            return;
        }

        // Android.Util.Log rather than ILogger, for the same reason the fault receiver does it: a
        // receiver can run before the MAUI container is guaranteed built. This is the acknowledged
        // exception in AGENTS.md, not a new direct-logging call site.
        if (intent.GetBooleanExtra("dump", false))
        {
            _ = DumpAsync(store, context);
            return;
        }

        if (intent.GetBooleanExtra("clear", false))
        {
            store.Clear();
            Android.Util.Log.Info("AniSprinkles", "TOKEN cleared — the app is now signed out");
            return;
        }

        var token = intent.GetStringExtra("token");
        if (string.IsNullOrWhiteSpace(token))
        {
            Android.Util.Log.Warn("AniSprinkles", "TOKEN broadcast ignored — no 'token' extra");
            return;
        }

        // Absent expiry means "no expiry recorded", which TokenStore treats as never-expiring.
        // That is the right default for a seeded dev token: AniList's implicit-grant tokens run
        // about a year, and a dev pass that got signed out mid-run because a guessed expiry elapsed
        // would be a worse failure than the one this exists to avoid.
        DateTimeOffset? expiresAt = null;
        var expires = intent.GetStringExtra("expires");
        if (!string.IsNullOrWhiteSpace(expires)
            && DateTimeOffset.TryParse(expires, out var parsed))
        {
            expiresAt = parsed;
        }

        // Fire-and-forget is deliberate and safe here: SetAsync publishes the in-memory copy under
        // a lock before it awaits the storage write, so the app is signed in by the time this
        // returns and the persist finishes behind it. Exceptions are observed rather than left to
        // the finalizer, since an unobserved one from a broadcast would be invisible.
        _ = SeedAsync(store, token, expiresAt);

        // Never log the token itself (#124). Its length is enough to tell "seeded the wrong thing"
        // from "seeded nothing" without putting a live credential in logcat.
        Android.Util.Log.Info(
            "AniSprinkles",
            $"TOKEN seeded ({token.Length} chars, expires={expiresAt?.ToString("O") ?? "<none>"}). "
            + "Already-loaded pages keep their signed-out state — pull to refresh or relaunch.");
    }

    private static async Task SeedAsync(TokenStore store, string token, DateTimeOffset? expiresAt)
    {
        try
        {
            await store.SetAsync(token, expiresAt);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("AniSprinkles", $"TOKEN seed failed to persist: {ex.Message}");
        }
    }

    /// <summary>The file <c>driver.ps1 dump-token</c> reads back, under the app's private files dir.</summary>
    public const string DumpFileName = "dev-token.txt";

    /// <summary>
    /// Writes the current token where adb can reach it with <c>run-as</c>, so a session signed in
    /// through the app's own OAuth flow can hand its token to the fixture tooling.
    /// <para>
    /// It goes to the app's private files directory and never to logcat or /sdcard: a live
    /// credential in a shared log is exactly what #124 exists to prevent. The driver deletes the
    /// file as soon as it has read it, so the window in which it exists on disk is seconds.
    /// </para>
    /// </summary>
    private static async Task DumpAsync(TokenStore store, Context? context)
    {
        var filesDir = context?.FilesDir?.AbsolutePath;
        if (filesDir is null)
        {
            Android.Util.Log.Warn("AniSprinkles", "TOKEN dump failed — no files directory");
            return;
        }

        var path = System.IO.Path.Combine(filesDir, DumpFileName);
        try
        {
            var lookup = await store.GetAsync();
            if (lookup.State != TokenState.Valid || lookup.AccessToken is null)
            {
                // Write the state rather than nothing, so the driver can tell "signed out" from
                // "the broadcast never arrived" instead of just timing out.
                await System.IO.File.WriteAllTextAsync(path, $"<{lookup.State}>");
                Android.Util.Log.Warn("AniSprinkles", $"TOKEN dump: nothing usable to dump ({lookup.State})");
                return;
            }

            await System.IO.File.WriteAllTextAsync(path, lookup.AccessToken);

            // Length only — never the value (#124).
            Android.Util.Log.Info(
                "AniSprinkles", $"TOKEN dumped ({lookup.AccessToken.Length} chars) to files/{DumpFileName}");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("AniSprinkles", $"TOKEN dump failed: {ex.Message}");
        }
    }
}
#endif
