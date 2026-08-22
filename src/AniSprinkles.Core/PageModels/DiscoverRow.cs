using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.PageModels;

/// <summary>
/// One Discover row: a <see cref="DiscoverSectionDefinition"/> plus the
/// <see cref="PaginatedSection{T}"/> holding its items, wrapped with the observable surface
/// (HasItems/IsLoadingMore) the row template binds to. The page model seeds every row from the
/// single aliased Discover request; horizontal scrolling pages each row independently through
/// BrowseAnime via <see cref="LoadMoreAsync"/>.
/// </summary>
public sealed partial class DiscoverRow : ObservableObject
{
    private readonly PaginatedSection<BrowseMediaItem> _section;

    public DiscoverRow(DiscoverSectionDefinition definition, PaginatedSection<BrowseMediaItem>.FetchPageDelegate fetchPage)
    {
        Definition = definition;
        _section = new PaginatedSection<BrowseMediaItem>(
            definition.Sort,
            fetchPage,
            item => item.Node?.Id ?? 0,
            StampBadges);
        _section.Changed += () =>
        {
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(IsLoadingMore));
        };
    }

    public DiscoverSectionDefinition Definition { get; }

    /// <summary>The <see cref="DiscoverSection"/> enum name — the View All route param and the Load More command param.</summary>
    public string SectionKey => Definition.Section.ToString();

    public string HeaderGlyph => Definition.Section switch
    {
        DiscoverSection.Airing => Glyphs.Regular.CalendarPlay24,
        DiscoverSection.Trending => Glyphs.Regular.ArrowTrending24,
        DiscoverSection.Top => Glyphs.Regular.Trophy24,
        DiscoverSection.TopMovies => Glyphs.Regular.MoviesAndTv24,
        DiscoverSection.AllTimePopular => Glyphs.Regular.People24,
        DiscoverSection.Upcoming => Glyphs.Regular.CalendarClock24,
        DiscoverSection.PopularAdult => Glyphs.Regular.EyeOff24,
        DiscoverSection.TopRatedAdult => Glyphs.Regular.Star24,
        _ => Glyphs.Regular.MoviesAndTv24,
    };

    public ObservableCollection<BrowseMediaItem> Items => _section.Items;

    public bool HasItems => Items.Count > 0;

    public bool IsLoadingMore => _section.IsLoadingMore;

    public bool CanLoadMore => _section.CanLoadMore;

    public void Seed(DiscoverSectionPage page) => _section.Seed(page.Items, page.PageInfo);

    public Task LoadMoreAsync(CancellationToken cancellationToken = default) => _section.LoadMoreAsync(cancellationToken);

    /// <summary>Stamps each card's metric badge with the section's sort metric (sort-metric rule).</summary>
    private void StampBadges(IReadOnlyList<BrowseMediaItem> added, string sort)
    {
        foreach (var item in added)
        {
            item.MetricBadge = MediaMetricBadges.ForMediaSort(item.Node, sort);
        }
    }
}
