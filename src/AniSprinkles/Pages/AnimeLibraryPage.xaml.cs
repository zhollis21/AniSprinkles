namespace AniSprinkles.Pages;

/// <summary>
/// The anime half of the Library tab. Everything but the page model lookup lives in
/// <see cref="MediaListPageBase"/>.
/// </summary>
public partial class AnimeLibraryPage : MediaListPageBase
{
    public AnimeLibraryPage()
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
        => services.GetService<AnimeLibraryPageModel>();
}
