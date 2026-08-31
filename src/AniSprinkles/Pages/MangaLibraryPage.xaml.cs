namespace AniSprinkles.Pages;

/// <summary>
/// The manga half of the Library tab (#12), replacing the placeholder #43 left behind. Identical
/// to <see cref="AnimeLibraryPage"/> apart from which page model it resolves — the list, its sections,
/// sorting, search and long-press flows are all the shared base's.
/// </summary>
public partial class MangaLibraryPage : MediaListPageBase
{
    public MangaLibraryPage()
    {
        InitializeComponent();
        AttachXamlElements(
            LoadedContentHost,
            SortToolbarItem,
            SearchToolbarItem,
            ViewModeToolbarItem,
            SortIcon,
            ViewModeIcon);
    }

    /// <inheritdoc />
    protected override MediaListPageModel? ResolveViewModel(IServiceProvider services)
        => services.GetService<MangaLibraryPageModel>();
}
