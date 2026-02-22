namespace AniSprinkles;

public partial class AppShell : Shell
{
    private const string MediaDetailsRoute = "media-details";

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(MediaDetailsRoute, typeof(MediaDetailsPage));
    }
}
