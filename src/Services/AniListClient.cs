using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

public class AniListClient : IAniListClient
{
    private static readonly Uri GraphQlEndpoint = new("https://graphql.anilist.co");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly IAuthService _authService;
    private readonly ILogger<AniListClient> _logger;
    private int? _cachedViewerId;
    private string? _cachedViewerToken;

    public AniListClient(HttpClient httpClient, IAuthService authService, ILogger<AniListClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = GraphQlEndpoint;
        }
    }

    public async Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default)
    {
        var token = await RequireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var viewerId = await GetViewerIdAsync(token, cancellationToken).ConfigureAwait(false);

        var data = await SendAsync<MediaListCollectionData>(
            "MediaListCollection",
            MediaListQuery,
            new { userId = viewerId },
            token,
            cancellationToken).ConfigureAwait(false);

        var results = new List<MediaListEntry>();
        foreach (var list in data.MediaListCollection?.Lists ?? [])
        {
            foreach (var entry in list.Entries ?? [])
            {
                var mapped = MapEntry(entry);
                if (mapped is not null)
                {
                    results.Add(mapped);
                }
            }
        }

        return results;
    }

    private async Task<int> GetViewerIdAsync(string token, CancellationToken cancellationToken)
    {
        // MediaListCollection requires userId; cache viewer id per token to avoid a repeated Viewer round-trip.
        if (_cachedViewerId.HasValue && string.Equals(_cachedViewerToken, token, StringComparison.Ordinal))
        {
            return _cachedViewerId.Value;
        }

        var viewer = await SendAsync<ViewerData>(
            "Viewer",
            ViewerQuery,
            null,
            token,
            cancellationToken).ConfigureAwait(false);

        var viewerId = viewer.Viewer?.Id
            ?? throw new InvalidOperationException("AniList viewer id missing.");

        _cachedViewerId = viewerId;
        _cachedViewerToken = token;
        return viewerId;
    }

    public async Task<IReadOnlyList<Media>> SearchAnimeAsync(string search, int page = 1, int perPage = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return Array.Empty<Media>();
        }

        var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var data = await SendAsync<SearchData>(
            "Search",
            SearchQuery,
            new { search, page, perPage },
            token,
            cancellationToken).ConfigureAwait(false);

        return data.Page?.Media?
            .Where(m => m is not null)
            .Select(MapMedia)
            .ToList() ?? [];
    }

    public async Task<Media?> GetMediaAsync(int id, CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var data = await SendAsync<MediaData>(
            "Media",
            MediaQuery,
            new { id },
            token,
            cancellationToken).ConfigureAwait(false);

        return data.Media is null ? null : MapMedia(data.Media);
    }

    public async Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default)
    {
        var token = await RequireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var variables = new
        {
            mediaId = entry.MediaId,
            status = ToStatusString(entry.Status),
            progress = entry.Progress,
            score = entry.Score,
            repeat = entry.Repeat,
            notes = entry.Notes,
            @private = entry.Private,
            hiddenFromStatusLists = entry.HiddenFromStatusLists
        };

        var data = await SendAsync<SaveEntryData>(
            "SaveMediaListEntry",
            SaveEntryMutation,
            variables,
            token,
            cancellationToken).ConfigureAwait(false);

        return data.SaveMediaListEntry is null ? null : MapEntry(data.SaveMediaListEntry);
    }

    public async Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var token = await RequireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        return await GetViewerIdAsync(token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RequireAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Not authenticated.");
        }

        return token;
    }

    private async Task<T> SendAsync<T>(string operationName, string query, object? variables, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException($"AniList GraphQL {operationName} query is empty.");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("GraphQL {Operation} request", operationName);

        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint);

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var payload = new GraphQlRequest
        {
            Query = query,
            Variables = variables,
            OperationName = operationName
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var trimmed = content?.Length > 500 ? content[..500] + "..." : content;
            throw new HttpRequestException($"AniList request failed ({(int)response.StatusCode}) for {operationName}. {trimmed}");
        }

        var graphQl = JsonSerializer.Deserialize<GraphQlResponse<T>>(content, JsonOptions);
        if (graphQl is null)
        {
            throw new InvalidOperationException("AniList response could not be parsed.");
        }

        if (graphQl.Errors is { Count: > 0 })
        {
            var message = graphQl.Errors[0].Message ?? "AniList request returned an error.";
            throw new InvalidOperationException(message);
        }

        if (graphQl.Data is null)
        {
            throw new InvalidOperationException("AniList response data missing.");
        }

        stopwatch.Stop();
        _logger.LogInformation("GraphQL {Operation} response ok in {Elapsed}ms", operationName, stopwatch.ElapsedMilliseconds);

        return graphQl.Data;
    }

    private static MediaListEntry? MapEntry(MediaListEntryDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var media = dto.Media is null ? null : MapMedia(dto.Media);

        return new MediaListEntry
        {
            Id = dto.Id,
            MediaId = dto.MediaId ?? media?.Id ?? 0,
            Media = media,
            Status = ParseStatus(dto.Status),
            Progress = dto.Progress,
            Score = dto.Score,
            Repeat = dto.Repeat,
            Notes = dto.Notes,
            Private = dto.Private,
            HiddenFromStatusLists = dto.HiddenFromStatusLists,
            UpdatedAt = dto.UpdatedAt is null ? null : DateTimeOffset.FromUnixTimeSeconds(dto.UpdatedAt.Value)
        };
    }

    private static Media MapMedia(MediaDto dto)
    {
        // Pre-shape potentially large metadata lists once here so details binding avoids extra UI-thread sorting/filtering.
        return new Media
        {
            Id = dto.Id,
            IdMal = dto.IdMal,
            Title = dto.Title,
            CoverImage = dto.CoverImage,
            BannerImage = dto.BannerImage,
            Description = dto.Description,
            Format = dto.Format,
            Status = dto.Status,
            Episodes = dto.Episodes,
            Duration = dto.Duration,
            Season = dto.Season,
            SeasonYear = dto.SeasonYear,
            Source = dto.Source,
            CountryOfOrigin = dto.CountryOfOrigin,
            IsAdult = dto.IsAdult,
            IsLicensed = dto.IsLicensed,
            SiteUrl = dto.SiteUrl,
            Hashtag = dto.Hashtag,
            AverageScore = dto.AverageScore,
            MeanScore = dto.MeanScore,
            Popularity = dto.Popularity,
            Favourites = dto.Favourites,
            Trending = dto.Trending,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            NextAiringEpisode = dto.NextAiringEpisode,
            Trailer = dto.Trailer,
            Synonyms = dto.Synonyms ?? [],
            Genres = dto.Genres ?? [],
            Tags = dto.Tags?
                .OrderByDescending(tag => tag.Rank ?? -1)
                .Take(15)
                .ToList() ?? [],
            Studios = dto.Studios?.Nodes ?? [],
            Rankings = dto.Rankings?
                .OrderBy(rank => rank.Rank ?? int.MaxValue)
                .Take(12)
                .ToList() ?? [],
            ExternalLinks = dto.ExternalLinks?
                .Where(link => link.IsDisabled is not true)
                .Take(12)
                .ToList() ?? [],
            StreamingEpisodes = dto.StreamingEpisodes?
                .Take(12)
                .ToList() ?? []
        };
    }

    private static MediaListStatus? ParseStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "CURRENT" => MediaListStatus.Current,
            "PLANNING" => MediaListStatus.Planning,
            "COMPLETED" => MediaListStatus.Completed,
            "DROPPED" => MediaListStatus.Dropped,
            "PAUSED" => MediaListStatus.Paused,
            "REPEATING" => MediaListStatus.Repeating,
            _ => null
        };
    }

    private static string? ToStatusString(MediaListStatus? status)
    {
        return status switch
        {
            MediaListStatus.Current => "CURRENT",
            MediaListStatus.Planning => "PLANNING",
            MediaListStatus.Completed => "COMPLETED",
            MediaListStatus.Dropped => "DROPPED",
            MediaListStatus.Paused => "PAUSED",
            MediaListStatus.Repeating => "REPEATING",
            _ => null
        };
    }

    private sealed class GraphQlRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;
        [JsonPropertyName("variables")]
        public object? Variables { get; set; }
        [JsonPropertyName("operationName")]
        public string? OperationName { get; set; }
    }

    private sealed class GraphQlResponse<T>
    {
        public T? Data { get; set; }
        public List<GraphQlError>? Errors { get; set; }
    }

    private sealed class GraphQlError
    {
        public string? Message { get; set; }
    }

    private sealed class MediaListCollectionData
    {
        public MediaListCollection? MediaListCollection { get; set; }
    }

    private sealed class MediaListCollection
    {
        public List<MediaListGroup>? Lists { get; set; }
    }

    private sealed class MediaListGroup
    {
        public string? Name { get; set; }
        public List<MediaListEntryDto>? Entries { get; set; }
    }

    private sealed class MediaListEntryDto
    {
        public int Id { get; set; }
        public int? MediaId { get; set; }
        public string? Status { get; set; }
        public int? Progress { get; set; }
        public double? Score { get; set; }
        public int? Repeat { get; set; }
        public string? Notes { get; set; }
        public bool? Private { get; set; }
        public bool? HiddenFromStatusLists { get; set; }
        public int? UpdatedAt { get; set; }
        public MediaDto? Media { get; set; }
    }

    private sealed class SearchData
    {
        public Page? Page { get; set; }
    }

    private sealed class Page
    {
        public List<MediaDto>? Media { get; set; }
    }

    private sealed class MediaData
    {
        public MediaDto? Media { get; set; }
    }

    private sealed class SaveEntryData
    {
        public MediaListEntryDto? SaveMediaListEntry { get; set; }
    }

    private sealed class ViewerData
    {
        public ViewerDto? Viewer { get; set; }
    }

    private sealed class ViewerDto
    {
        public int Id { get; set; }
    }

    private sealed class MediaDto
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
        public int? Trending { get; set; }
        public MediaDate? StartDate { get; set; }
        public MediaDate? EndDate { get; set; }
        public MediaAiringEpisode? NextAiringEpisode { get; set; }
        public MediaTrailer? Trailer { get; set; }
        public List<string>? Synonyms { get; set; }
        public List<string>? Genres { get; set; }
        public List<MediaTag>? Tags { get; set; }
        public StudioConnection? Studios { get; set; }
        public List<MediaRanking>? Rankings { get; set; }
        public List<MediaExternalLink>? ExternalLinks { get; set; }
        public List<MediaStreamingEpisode>? StreamingEpisodes { get; set; }
    }

    private sealed class StudioConnection
    {
        public List<Studio>? Nodes { get; set; }
    }

    private const string ViewerQuery = @"
query Viewer {
  Viewer {
    id
  }
}";

    private const string MediaListQuery = @"
query MediaListCollection($userId: Int) {
  MediaListCollection(userId: $userId, type: ANIME) {
    lists {
      name
      entries {
        id
        mediaId
        status
        progress
        score
        repeat
        updatedAt
        private
        hiddenFromStatusLists
        notes
        media {
          id
          title { romaji english native }
          coverImage { medium large }
          format
          status
          episodes
          season
          seasonYear
          averageScore
          popularity
        }
      }
    }
  }
}";

    private const string SearchQuery = @"
query Search($search: String!, $page: Int, $perPage: Int) {
  Page(page: $page, perPage: $perPage) {
    media(type: ANIME, search: $search, sort: SEARCH_MATCH) {
      id
      title { romaji english native }
      coverImage { medium large }
      format
      status
      episodes
      season
      seasonYear
      averageScore
      popularity
      genres
    }
  }
}";

    private const string MediaQuery = @"
query Media($id: Int!) {
  Media(id: $id, type: ANIME) {
    id
    idMal
    title { romaji english native }
    coverImage { medium large extraLarge color }
    bannerImage
    description
    format
    status
    episodes
    duration
    season
    seasonYear
    source
    countryOfOrigin
    isAdult
    isLicensed
    siteUrl
    hashtag
    startDate { year month day }
    endDate { year month day }
    nextAiringEpisode { airingAt timeUntilAiring episode }
    trailer { id site thumbnail }
    synonyms
    genres
    averageScore
    meanScore
    popularity
    favourites
    trending
    tags { id name rank isMediaSpoiler isGeneralSpoiler isAdult description category }
    studios(isMain: true) { nodes { id name isAnimationStudio } }
    rankings { rank type format year season allTime context }
    externalLinks { id url site siteId type language color isDisabled }
    streamingEpisodes { title thumbnail url site }
  }
}";

    private const string SaveEntryMutation = @"
mutation SaveMediaListEntry($mediaId: Int, $status: MediaListStatus, $progress: Int, $score: Float, $repeat: Int, $notes: String, $private: Boolean, $hiddenFromStatusLists: Boolean) {
  SaveMediaListEntry(mediaId: $mediaId, status: $status, progress: $progress, score: $score, repeat: $repeat, notes: $notes, private: $private, hiddenFromStatusLists: $hiddenFromStatusLists) {
    id
    mediaId
    status
    progress
    score
    repeat
    updatedAt
    private
    hiddenFromStatusLists
    notes
    media {
      id
      title { romaji english native }
      coverImage { medium large }
      format
      status
      episodes
      season
      seasonYear
      averageScore
      popularity
    }
  }
}";
}
