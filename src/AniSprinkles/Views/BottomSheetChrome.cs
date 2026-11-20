namespace AniSprinkles.Views;

/// <summary>
/// The edge-to-edge half of a CommunityToolkit bottom sheet, for <see cref="UpNextPopup"/> (#103).
/// Same steps as <see cref="MoveToListPopup"/> and <see cref="ActionMenuPopup"/> carry inline; see
/// MoveToListPopup for why each is needed.
/// </summary>
internal static class BottomSheetChrome
{
    /// <summary>
    /// Call from the popup's <c>Opened</c>: the toolkit's host Grid only exists once attached. Clears
    /// the safe-area inset on every ancestor up to the page so the sheet reaches the physical bottom
    /// edge, then pads <paramref name="content"/> up by the nav-bar inset so its last row clears it.
    /// </summary>
    public static void GoEdgeToEdge(Element popup, Layout content)
    {
        for (Element? element = popup; element is not null and not Page; element = element.Parent)
        {
            switch (element)
            {
                case Layout layout:
                    layout.SafeAreaEdges = SafeAreaEdges.None;
                    break;
                case ContentView contentView:
                    contentView.SafeAreaEdges = SafeAreaEdges.None;
                    break;
                case Border border:
                    border.SafeAreaEdges = SafeAreaEdges.None;
                    break;
            }
        }

        var bottomInsetDip = BottomSystemBarInsetDip();
        if (bottomInsetDip > 0)
        {
            content.Padding = new Thickness(0, 0, 0, bottomInsetDip);
        }
    }

    private static double BottomSystemBarInsetDip()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        var insets = activity?.Window?.DecorView?.RootWindowInsets?
            .GetInsets(Android.Views.WindowInsets.Type.SystemBars());
        var density = activity?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (insets is not null && density > 0)
        {
            return insets.Bottom / density;
        }
#endif
        return 0;
    }
}
