namespace AniSprinkles.PageModels;

/// <summary>
/// Mutually-exclusive main states for a page's state machine. Orthogonal concerns
/// (overlays, banners, in-flight operations, identity) remain as independent
/// <c>[ObservableProperty]</c> booleans — only fold states here when they cannot
/// co-exist.
/// </summary>
public enum PageState
{
    /// <summary>Initial state: auth check has not yet resolved.</summary>
    AuthenticationPending,

    /// <summary>Auth resolved; no signed-in user.</summary>
    Unauthenticated,

    /// <summary>First data fetch after authentication.</summary>
    InitialLoading,

    /// <summary>Data loaded; page content is displayed.</summary>
    Content,

    /// <summary>Load failed with no cached data to fall back on.</summary>
    Error,
}
