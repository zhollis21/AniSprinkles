namespace AniSprinkles.Services
{
    public class MockAuthService : IAuthService
    {
        public bool IsAuthenticated => true;
        public string? UserName => "You";
    }
}
