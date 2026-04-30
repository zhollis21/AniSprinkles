namespace AniSprinkles;

public partial class AppShell : Shell
{
    private const string MediaDetailsRoute = "media-details";
    private const string StaffDetailsRoute = "staff-details";
    private const string CharacterDetailsRoute = "character-details";

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(MediaDetailsRoute, typeof(MediaDetailsPage));
        Routing.RegisterRoute(StaffDetailsRoute, typeof(StaffDetailsPage));
        Routing.RegisterRoute(CharacterDetailsRoute, typeof(CharacterDetailsPage));
    }
}
