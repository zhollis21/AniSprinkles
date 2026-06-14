namespace AniSprinkles.Models;

/// <summary>
/// One section's first page from the aliased Discover query: the items plus the paging state
/// needed to seed a <c>PaginatedSection</c> so the row can keep scrolling past page 1.
/// </summary>
public sealed record DiscoverSectionPage(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)
{
    public static readonly DiscoverSectionPage Empty = new([], null);
}

/// <summary>
/// Result of the single aliased Discover query — one page of media per section.
/// The 18+ pair is only populated when the adult toggle is on.
/// </summary>
public class DiscoverSections
{
    public DiscoverSectionPage Airing { get; set; } = DiscoverSectionPage.Empty;
    public DiscoverSectionPage Trending { get; set; } = DiscoverSectionPage.Empty;
    public DiscoverSectionPage Top { get; set; } = DiscoverSectionPage.Empty;
    public DiscoverSectionPage TopMovies { get; set; } = DiscoverSectionPage.Empty;
    public DiscoverSectionPage AllTimePopular { get; set; } = DiscoverSectionPage.Empty;
    public DiscoverSectionPage Upcoming { get; set; } = DiscoverSectionPage.Empty;
    public DiscoverSectionPage PopularAdult { get; set; } = DiscoverSectionPage.Empty;
    public DiscoverSectionPage TopRatedAdult { get; set; } = DiscoverSectionPage.Empty;
}
