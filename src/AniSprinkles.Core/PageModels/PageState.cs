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

    /// <summary>
    /// Loaded successfully, but the viewer has nothing on this list (#12). Distinct from
    /// <see cref="Error"/> because nothing went wrong, and distinct from <see cref="Content"/>
    /// because rendering zero sections is a blank page. Reachable on either half of the Library
    /// tab, but far likelier on manga: plenty of AniList accounts track anime only.
    /// </summary>
    Empty,
}
