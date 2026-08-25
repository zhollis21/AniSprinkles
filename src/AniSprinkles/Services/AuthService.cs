using Android.Webkit;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

public class AuthService : IAuthService
{
    private const string ClientId = "35674";
    private const string RedirectUri = "anisprinkles://auth";

    private readonly TokenStore _tokens;
    private readonly ILogger<AuthService> _logger;

    public AuthService(TokenStore tokens, ILogger<AuthService> logger)
    {
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Token state, the single-flight first load and the expiry check all live in TokenStore
        // (#119). What stays here is the platform half: the OAuth round trip and the WebView cookie
        // store, neither of which the test project can reference.
        var lookup = await _tokens.GetAsync(cancellationToken);

        if (lookup.State is TokenState.Absent)
        {
            // "No usable token" rather than "no token in SecureStorage": TokenStore reports Absent
            // both when nothing is stored and when the read failed, and collapsing those is
            // deliberate (#116) since callers act identically. A failed read logs its own Error line
            // with the cause, so nothing is lost by not asserting which case this is here.
            _logger.LogInformation("AUTH token-check: absent (no usable token).");
            return null;
        }

        if (lookup.State is TokenState.Expired)
        {
            _logger.LogInformation("AUTH token-check: expired (expiresAt={ExpiresAt}), signing out.", lookup.ExpiresAt);

            // Guarded because SignOutAsync clears SecureStorage and drives the Android CookieManager
            // on the main thread, either of which can throw, and this runs inside the token check
            // that every tab's async void OnAppearing awaits. The answer is "no valid token" either
            // way, so a failed cleanup must not become a crash. Explicit sign-out (the Settings
            // button) still calls SignOutAsync directly and keeps surfacing its failures.
            try
            {
                await SignOutAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Propagate ONLY the caller's own cancellation. The filter is load-bearing: ten
                // call sites across six page models call GetAccessTokenAsync with no token at all,
                // from async void OnAppearing paths with nothing to catch. SignOutAsync drives the
                // Android CookieManager through MainThread.InvokeOnMainThreadAsync, which can
                // surface a cancellation of its own during teardown — rethrowing that to a caller
                // who never asked for cancellation turns a survivable cleanup failure into a
                // crash. Unfiltered, this catch did exactly that.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AUTH sign-out after expiry failed; reporting no token anyway.");
                _tokens.Forget();
            }

            return null;
        }

        _logger.LogInformation("AUTH token-check: valid (expiresAt={ExpiresAt}).", lookup.ExpiresAt);
        return lookup.AccessToken;
    }

    public async Task<bool> SignInAsync(CancellationToken cancellationToken = default)
    {
        // RunContinuationsAsynchronously prevents the continuation of tcs.Task from running
        // inline inside OAuthWebViewPage.OnNavigating when TrySetResult is called on the UI thread.
        var tcs = new TaskCompletionSource<IDictionary<string, string>?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Cancel the TCS (and therefore the sign-in wait) if the caller cancels.
        using var _ = cancellationToken.Register(() => tcs.TrySetResult(null));

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = new OAuthWebViewPage(BuildAuthorizeUri(), RedirectUri, tcs);
            await Shell.Current.Navigation.PushModalAsync(page, animated: true);
        });

        var properties = await tcs.Task;

        if (properties is null ||
            !properties.TryGetValue("access_token", out var accessToken) ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var expiresAt = ParseExpiresAt(properties);

        _logger.LogInformation("AniList sign-in successful. Expires at {ExpiresAt}.", expiresAt);

        await _tokens.SetAsync(accessToken, expiresAt);

        return true;
    }

    public async Task SignOutAsync()
    {
        _logger.LogInformation("AniList sign-out.");
        _tokens.Clear();

        // Clear the in-app WebView cookie store so the next sign-in always prompts for credentials.
        // CookieManager manages the Android WebView cookie store (separate from Chrome's store),
        // so clearing it here does not affect the user's Chrome browsing session.
        // RemoveAllCookies is async (callback-based) — we await completion before Flushing to disk
        // so a fast sign-out → sign-in cannot race against incomplete cookie removal.
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var cookieManager = CookieManager.Instance;
            if (cookieManager is null)
            {
                return;
            }

            var cookieTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cookieManager.RemoveAllCookies(new CookieRemovalCallback(cookieTcs));
            await cookieTcs.Task;
            cookieManager.Flush();
        });
    }

    private static Uri BuildAuthorizeUri()
    {
        var query = $"client_id={ClientId}&response_type=token";
        return new Uri($"https://anilist.co/api/v2/oauth/authorize?{query}");
    }

    private static DateTimeOffset? ParseExpiresAt(IDictionary<string, string> properties)
    {
        if (!properties.TryGetValue("expires_in", out var raw))
        {
            return null;
        }

        return int.TryParse(raw, out var seconds)
            ? DateTimeOffset.UtcNow.AddSeconds(seconds)
            : null;
    }

    private sealed class CookieRemovalCallback : Java.Lang.Object, IValueCallback
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public CookieRemovalCallback(TaskCompletionSource<bool> tcs)
        {
            _tcs = tcs;
        }

        public void OnReceiveValue(Java.Lang.Object? value)
        {
            _tcs.TrySetResult(value is Java.Lang.Boolean b && b.BooleanValue());
        }
    }
}
