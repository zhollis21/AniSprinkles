namespace AniSprinkles.Views;

public partial class DiscoverLoadedContentView : ContentView
{
    // The long-press hook and the search-field focus subscription that used to live here went to
    // SearchPage with the search itself (#43). The section carousels attach their own long-press
    // inside DiscoverSectionView.
    public DiscoverLoadedContentView()
    {
        InitializeComponent();
    }
}
