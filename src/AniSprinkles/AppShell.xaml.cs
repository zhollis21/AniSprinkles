using IconFont.Maui.FluentIcons;

namespace AniSprinkles;

public partial class AppShell : Shell
{
    private const string MediaDetailsRoute = "media-details";
    private const string StaffDetailsRoute = "staff-details";
    private const string CharacterDetailsRoute = "character-details";
    private const string StudioDetailsRoute = "studio-details";
    private const string MediaBrowseRoute = "media-browse";

    // Selected tabs show the Filled glyph, unselected the Regular one (issue #43).
    // MAUI Shell has no per-state icon property — Android renders the bar through
    // BottomNavigationView and Shell only maps a single Icon — so the swap is done
    // here on Navigated. Both ImageSources per tab are built once and reused; a new
    // FontImageSource per navigation would churn allocations on every tab tap.
    private (Tab Tab, FontImageSource Regular, FontImageSource Filled)[] _tabIcons = [];

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(MediaDetailsRoute, typeof(MediaDetailsPage));
        Routing.RegisterRoute(StaffDetailsRoute, typeof(StaffDetailsPage));
        Routing.RegisterRoute(CharacterDetailsRoute, typeof(CharacterDetailsPage));
        Routing.RegisterRoute(StudioDetailsRoute, typeof(StudioDetailsPage));
        Routing.RegisterRoute(MediaBrowseRoute, typeof(MediaBrowsePage));

        _tabIcons =
        [
            BuildIcons(LibraryTab,  FluentIconsRegular.Library24,           FluentIconsFilled.Library24),
            BuildIcons(DiscoverTab, FluentIconsRegular.Rocket24,  FluentIconsFilled.Rocket24),
            BuildIcons(SearchTab,   FluentIconsRegular.Search24,            FluentIconsFilled.Search24),
            BuildIcons(FeedTab,     FluentIconsRegular.Feed24,              FluentIconsFilled.Feed24),
            BuildIcons(SettingsTab, FluentIconsRegular.Settings24,          FluentIconsFilled.Settings24),
        ];

        // Navigated covers tab taps and programmatic GoToAsync alike. Run once up front
        // so the tab the app launches on starts filled rather than waiting for a switch.
        Navigated += OnShellNavigated;
        ApplySelectedTabIcons();
    }

    private static (Tab, FontImageSource, FontImageSource) BuildIcons(Tab tab, string regularGlyph, string filledGlyph)
        => (tab,
            new FontImageSource { Glyph = regularGlyph, FontFamily = FluentIconsRegular.FontFamily },
            new FontImageSource { Glyph = filledGlyph, FontFamily = FluentIconsFilled.FontFamily });

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e) => ApplySelectedTabIcons();

    private void ApplySelectedTabIcons()
    {
        // Shell.CurrentItem is the TabBar; its CurrentItem is the selected Tab.
        // No Color is set on either source, so the platform keeps applying
        // Shell.TabBarForegroundColor / TabBarUnselectedColor from Styles.xaml.
        var selected = CurrentItem?.CurrentItem;

        foreach (var (tab, regular, filled) in _tabIcons)
        {
            var wanted = ReferenceEquals(tab, selected) ? filled : regular;
            if (!ReferenceEquals(tab.Icon, wanted))
            {
                tab.Icon = wanted;
            }
        }
    }
}
