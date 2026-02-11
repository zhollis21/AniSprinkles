namespace AniSprinkles;

public partial class AppShell : Shell
{
    private const string MediaDetailsRoute = "media-details";
    private const string MediaDetailsSmokeRoute = "media-details-smoke";

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(MediaDetailsRoute, typeof(MediaDetailsPage));
        Routing.RegisterRoute(MediaDetailsSmokeRoute, typeof(MediaDetailsSmokeTestPage));
    }
}
