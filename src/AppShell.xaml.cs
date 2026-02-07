namespace AniSprinkles
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("media-details", typeof(MediaDetailsPage));
        }
    }
}
