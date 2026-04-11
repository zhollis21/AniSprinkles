using Android.Webkit;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

public class AuthService : IAuthService
{
    private const string ClientId = "35674";
    private const string RedirectUri = "anisprinkles://auth";
    private const string TokenKey = "anilist_access_token";
    private const string TokenExpiryKey = "anilist_access_token_expires_at";

    private readonly ILogger<AuthService> _logger;
    private string? AccessToken { get; set; }
    private DateTimeOffset? ExpiresAt { get; set; }

    public AuthService(ILogger<AuthService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (AccessToken is null)
        {
            await LoadAsync(cancellationToken);
        }

        if (IsExpired())
        {
            _logger.LogInformation("AniList access token expired.");
            await SignOutAsync();
            return null;
        }

        return AccessToken;
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

        AccessToken = accessToken;
        ExpiresAt = ParseExpiresAt(properties);

        _logger.LogInformation("AniList sign-in successful. Expires at {ExpiresAt}.", ExpiresAt);

        await SecureStorage.Default.SetAsync(TokenKey, AccessToken);
        if (ExpiresAt is not null)
        {
            await SecureStorage.Default.SetAsync(TokenExpiryKey, ExpiresAt.Value.ToString("O"));
        }
        else
        {
            SecureStorage.Default.Remove(TokenExpiryKey);
        }

        return true;
    }

    public async Task SignOutAsync()
    {
        _logger.LogInformation("AniList sign-out.");
        AccessToken = null;
        ExpiresAt = null;
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(TokenExpiryKey);

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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        AccessToken = await SecureStorage.Default.GetAsync(TokenKey);
        var rawExpiry = await SecureStorage.Default.GetAsync(TokenExpiryKey);
        if (DateTimeOffset.TryParse(rawExpiry, out var expiry))
        {
            ExpiresAt = expiry;
        }
    }

    private bool IsExpired()
        => ExpiresAt is not null && ExpiresAt <= DateTimeOffset.UtcNow;

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
