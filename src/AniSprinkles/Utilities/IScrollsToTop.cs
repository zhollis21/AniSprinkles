namespace AniSprinkles.Utilities;

/// <summary>
/// A tab's root page, or the content view it hosts, that can put itself back at the top when its
/// bottom tab is re-tapped (#200). Implementations no-op when there is nothing to scroll yet,
/// for example while the deferred content view has not been built.
/// </summary>
public interface IScrollsToTop
{
    void ScrollToTop();
}
