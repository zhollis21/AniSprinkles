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
        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(
                BuildAuthorizeUri(),
                new Uri(RedirectUri));

            var accessToken = result?.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken) &&
                result?.Properties.TryGetValue("access_token", out var tokenValue) == true)
            {
                accessToken = tokenValue;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            AccessToken = accessToken;
            ExpiresAt = ParseExpiresAt(result);

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
        catch (TaskCanceledException)
        {
            _logger.LogInformation("AniList sign-in canceled.");
            return false;
        }
    }

    public Task SignOutAsync()
    {
        _logger.LogInformation("AniList sign-out.");
        AccessToken = null;
        ExpiresAt = null;
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(TokenExpiryKey);
        return Task.CompletedTask;
    }

    private static Uri BuildAuthorizeUri()
    {
        var query = $"client_id={ClientId}&response_type=token";
        return new Uri($"https://anilist.co/api/v2/oauth/authorize?{query}");
    }

    private static DateTimeOffset? ParseExpiresAt(WebAuthenticatorResult? result)
    {
        if (result?.Properties.TryGetValue("expires_in", out var raw) != true)
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
}
