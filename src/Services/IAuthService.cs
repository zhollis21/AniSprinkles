namespace AniSprinkles.Services
{
    public interface IAuthService
    {
        bool IsAuthenticated { get; }
        string? UserName { get; }
    }
}
