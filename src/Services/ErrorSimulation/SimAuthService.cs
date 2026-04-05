#if ERROR_SIM
namespace AniSprinkles.Services;

/// <summary>
/// Error-simulation stub that reports the app as always authenticated,
/// so every page reaches its data-fetch call and hits the FailingAniListClient.
/// Compiled out of all builds except when -p:ErrorSim=true is passed.
/// </summary>
internal sealed class SimAuthService : IAuthService
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>("sim-stub-token");

    public Task<bool> SignInAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task SignOutAsync() => Task.CompletedTask;
}
#endif
