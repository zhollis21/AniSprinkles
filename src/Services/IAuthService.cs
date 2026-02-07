namespace AniSprinkles.Services
{
    public interface IAuthService
    {
        Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
        Task<bool> SignInAsync(CancellationToken cancellationToken = default);
        Task SignOutAsync();
    }
}
