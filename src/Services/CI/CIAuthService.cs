#if CI
namespace AniSprinkles.Services;

/// <summary>
/// CI-only stub that reports the app as always authenticated.
/// Compiled out of Debug and Release builds entirely — only active when -p:CiBuild=true.
/// </summary>
internal sealed class CIAuthService : IAuthService
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>("ci-stub-token");

    public Task<bool> SignInAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task SignOutAsync() => Task.CompletedTask;
}
#endif
