using AniSprinkles.Utilities;

namespace AniSprinkles.Views;

public partial class DiscoverLoadedContentView : ContentView, IScrollsToTop
{
    // Re-tapping the Discover tab at its root (#200). Animated: the page is a handful of carousel rows,
    // so the scroll is short. Fire-and-forget is fine, since ScrollToAsync only completes the animation.
    public void ScrollToTop() => _ = ContentScrollView.ScrollToAsync(0, 0, animated: true);

    // The long-press hook and the search-field focus subscription that used to live here went to
    // SearchPage with the search itself (#43). The section carousels attach their own long-press
    // inside DiscoverSectionView.
    public DiscoverLoadedContentView()
    {
        InitializeComponent();
    }
}
