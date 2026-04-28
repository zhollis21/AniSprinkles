#if CI
namespace AniSprinkles.Services;

/// <summary>
/// CI-only stub that starts authenticated for screenshot builds while still
/// allowing manual sign-out/sign-in to exercise the OAuth modal flow.
/// Compiled out of Debug and Release builds entirely — only active when -p:CiBuild=true.
/// </summary>
internal sealed class CIAuthService : IAuthService
{
    private const string StubToken = "ci-stub-token";
    private const string RedirectUri = "anisprinkles://auth";

    private string? _accessToken = StubToken;

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_accessToken);

    public async Task<bool> SignInAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<IDictionary<string, string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = cancellationToken.Register(() => tcs.TrySetResult(null));

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = OAuthWebViewPage.CreateCiMock(RedirectUri, tcs);
            await Shell.Current.Navigation.PushModalAsync(page, animated: true);
        });

        var properties = await tcs.Task;
        if (properties is null ||
            !properties.TryGetValue("access_token", out var accessToken) ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        _accessToken = StubToken;
        return true;
    }

    public Task SignOutAsync()
    {
        _accessToken = null;
        return Task.CompletedTask;
    }
}
#endif
