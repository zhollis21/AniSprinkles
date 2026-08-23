using AniSprinkles.Utilities;

namespace AniSprinkles.Models;

public class Media : IFavouritable
{
    public int Id { get; set; }
    public int? IdMal { get; set; }
    public MediaTitle? Title { get; set; }
    public MediaCoverImage? CoverImage { get; set; }
    public string? BannerImage { get; set; }
    public string? Description { get; set; }
    public string? Format { get; set; }
    public string? Status { get; set; }
    public int? Episodes { get; set; }
    public int? Duration { get; set; }
    public string? Season { get; set; }
    public int? SeasonYear { get; set; }
    public string? Source { get; set; }
    public string? CountryOfOrigin { get; set; }
    public bool? IsAdult { get; set; }
    public bool? IsLicensed { get; set; }
    public string? SiteUrl { get; set; }
    public string? Hashtag { get; set; }
    public int? AverageScore { get; set; }
    public int? MeanScore { get; set; }
    public int? Popularity { get; set; }
    public int? Favourites { get; set; }
    /// <summary>Compact favourites (k-format, blank when missing) — mirrors the other detail models.</summary>
    public string FavouritesDisplay => MetricFormat.Compact(Favourites);
    /// <summary>Whether the signed-in viewer has favorited this entity (from <c>isFavourite</c>).</summary>
    public bool IsFavourite { get; set; }
    public int? Trending { get; set; }
    public MediaDate? StartDate { get; set; }
    public MediaDate? EndDate { get; set; }
    public MediaAiringEpisode? NextAiringEpisode { get; set; }
    public MediaTrailer? Trailer { get; set; }
    public List<string> Synonyms { get; set; } = [];
    public List<string> Genres { get; set; } = [];
    public List<MediaTag> Tags { get; set; } = [];
    public List<Studio> Studios { get; set; } = [];
    public List<MediaRanking> Rankings { get; set; } = [];
    public List<MediaExternalLink> ExternalLinks { get; set; } = [];
    public List<MediaRelationEdge> Relations { get; set; } = [];
    public List<CharacterEdge> Characters { get; set; } = [];
    public List<MediaRecommendationNode> Recommendations { get; set; } = [];
    public List<ScoreDistributionItem> ScoreDistribution { get; set; } = [];
    public List<StatusDistribution> StatusDistribution { get; set; } = [];
    public List<StaffEdge> Staff { get; set; } = [];

    // Page cursors for the sections the heavy MediaQuery seeds at perPage 25. The details page seeds
    // each PaginatedSection with the first page's items + this cursor so Load More / sort refetches stay
    // consistent. Relations is intentionally unpaged (client-side sort only), so it has no PageInfo.
    public PageInfo? CharactersPageInfo { get; set; }
    public PageInfo? StaffPageInfo { get; set; }
    public PageInfo? RecommendationsPageInfo { get; set; }

    public string DisplayTitle => AppSettings.TitleLanguage switch
    {
        UserTitleLanguage.English
            => Title?.English ?? Title?.Romaji ?? Title?.Native ?? "Unknown Title",
        UserTitleLanguage.Native
            => Title?.Native ?? Title?.Romaji ?? Title?.English ?? "Unknown Title",
        _ => Title?.Romaji ?? Title?.English ?? Title?.Native ?? "Unknown Title",
    };
}
