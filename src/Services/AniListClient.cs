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

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
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

    public async Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>> GetMyAnimeListGroupedAsync(CancellationToken cancellationToken = default)
    {
        var token = await RequireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var viewerId = await GetViewerIdAsync(token, cancellationToken).ConfigureAwait(false);

        var data = await SendAsync<MediaListCollectionData>(
            "MediaListCollection",
            MediaListQuery,
            new { userId = viewerId },
            token,
            cancellationToken).ConfigureAwait(false);

        var groups = new List<(string Name, IReadOnlyList<MediaListEntry> Entries)>();
        foreach (var list in data.MediaListCollection?.Lists ?? [])
        {
            var name = list.Name ?? "Unknown";
            var entries = new List<MediaListEntry>();
            foreach (var entry in list.Entries ?? [])
            {
                var mapped = MapEntry(entry);
                if (mapped is not null)
                {
                    entries.Add(mapped);
                }
            }

            if (entries.Count > 0)
            {
                groups.Add((name, entries));
            }
        }

        return groups;
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

    public async Task<(Media? Media, MediaListEntry? ListEntry)> GetMediaAsync(int id, CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var data = await SendAsync<MediaData>(
            "Media",
            MediaQuery,
            new { id },
            token,
            cancellationToken).ConfigureAwait(false);

        if (data.Media is null)
        {
            return (null, null);
        }

        _logger.LogInformation(
            "DATATRACE GetMediaAsync raw API: mediaListEntry.progress={Progress}, score={Score}, id={EntryId}",
            data.Media.MediaListEntry?.Progress, data.Media.MediaListEntry?.Score, data.Media.MediaListEntry?.Id);

        var media = MapMedia(data.Media);
        var listEntry = MapEntry(data.Media.MediaListEntry);

        _logger.LogInformation(
            "DATATRACE GetMediaAsync mapped: listEntry.Progress={Progress}, Score={Score}, Id={EntryId}",
            listEntry?.Progress, listEntry?.Score, listEntry?.Id);

        return (media, listEntry);
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

    public async Task<bool> DeleteMediaListEntryAsync(int entryId, CancellationToken cancellationToken = default)
    {
        var token = await RequireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var data = await SendAsync<DeleteEntryData>(
            "DeleteMediaListEntry",
            DeleteEntryMutation,
            new { id = entryId },
            token,
            cancellationToken).ConfigureAwait(false);

        return data.DeleteMediaListEntry?.Deleted == true;
    }

    public async Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var token = await RequireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        return await GetViewerIdAsync(token, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AniListUser> GetViewerAsync(CancellationToken cancellationToken = default)
    {
        var token = await RequireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var data = await SendAsync<ViewerFullData>(
            "ViewerFull",
            ViewerFullQuery,
            null,
            token,
            cancellationToken).ConfigureAwait(false);

        var viewer = data.Viewer ?? throw new InvalidOperationException("AniList viewer data missing.");

        // Cache viewer id while we have it
        _cachedViewerId = viewer.Id;
        _cachedViewerToken = token;

        return MapUser(viewer);
    }

    public async Task<AniListUser> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var token = await RequireAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        var variables = new Dictionary<string, object?>();
        if (request.TitleLanguage.HasValue)
        {
            variables["titleLanguage"] = ToTitleLanguageString(request.TitleLanguage.Value);
        }

        if (request.DisplayAdultContent.HasValue)
        {
            variables["displayAdultContent"] = request.DisplayAdultContent.Value;
        }

        if (request.AiringNotifications.HasValue)
        {
            variables["airingNotifications"] = request.AiringNotifications.Value;
        }

        if (request.ScoreFormat.HasValue)
        {
            variables["scoreFormat"] = ToScoreFormatString(request.ScoreFormat.Value);
        }

        if (request.ProfileColor is not null)
        {
            variables["profileColor"] = request.ProfileColor;
        }

        if (request.StaffNameLanguage.HasValue)
        {
            variables["staffNameLanguage"] = ToStaffNameLanguageString(request.StaffNameLanguage.Value);
        }

        if (request.RestrictMessagesToFollowing.HasValue)
        {
            variables["restrictMessagesToFollowing"] = request.RestrictMessagesToFollowing.Value;
        }

        if (request.ActivityMergeTime.HasValue)
        {
            variables["activityMergeTime"] = request.ActivityMergeTime.Value;
        }

        if (request.NotificationOptions is not null)
        {
            variables["notificationOptions"] = request.NotificationOptions.Select(n => new { type = n.Type, enabled = n.Enabled }).ToList();
        }

        var data = await SendAsync<UpdateUserData>(
            "UpdateUser",
            UpdateUserMutation,
            variables,
            token,
            cancellationToken).ConfigureAwait(false);

        var user = data.UpdateUser ?? throw new InvalidOperationException("AniList update user response missing.");
        return MapUser(user);
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
            JsonSerializer.Serialize(payload, JsonWriteOptions),
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
            Relations = dto.Relations?.Edges?
                .Where(e => e.Node is not null)
                .Select(e => new MediaRelationEdge
                {
                    RelationType = FormatRelationType(e.RelationType),
                    Node = MapRelatedMedia(e.Node!),
                })
                .ToList() ?? [],
            Characters = dto.Characters?.Edges?
                .Where(e => e.Node is not null)
                .Select(e => new CharacterEdge
                {
                    Node = new Character
                    {
                        Id = e.Node!.Id,
                        Name = e.Node.Name,
                        Image = e.Node.Image,
                    },
                    Role = e.Role,
                    VoiceActors = e.VoiceActors?
                        .Where(va => va.Language is "Japanese" or "English")
                        .Select(va => new VoiceActor
                        {
                            Id = va.Id,
                            Name = va.Name,
                            Image = va.Image,
                            Language = va.Language,
                        })
                        .ToList() ?? [],
                })
                .ToList() ?? [],
            Recommendations = dto.Recommendations?.Nodes?
                .Where(n => n.MediaRecommendation is not null)
                .Select(n => new MediaRecommendationNode
                {
                    Rating = n.Rating,
                    MediaRecommendation = MapRelatedMedia(n.MediaRecommendation!),
                })
                .ToList() ?? [],
            ScoreDistribution = MapScoreDistribution(dto.Stats?.ScoreDistribution),
            StatusDistribution = dto.Stats?.StatusDistribution ?? [],
            Staff = dto.Staff?.Edges?
                .Where(e => e.Node is not null)
                .Select(e => new StaffEdge
                {
                    Node = new StaffNode
                    {
                        Id = e.Node!.Id,
                        Name = e.Node.Name,
                        Image = e.Node.Image,
                    },
                    Role = e.Role,
                })
                .ToList() ?? [],
        };
    }

    private static RelatedMedia MapRelatedMedia(RelatedMediaDto dto) => new()
    {
        Id = dto.Id,
        Title = dto.Title,
        Format = dto.Format,
        Type = dto.Type,
        Status = dto.Status,
        CoverImage = dto.CoverImage,
        AverageScore = dto.AverageScore,
    };

    private static string? FormatRelationType(string? raw) =>
        raw?.Replace("_", " ") is { } s
            ? System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant())
            : null;

    private static List<ScoreDistributionItem> MapScoreDistribution(List<ScoreDistributionDto>? items)
    {
        if (items is null || items.Count == 0)
        {
            return [];
        }

        var maxAmount = items.Max(s => s.Amount ?? 0);
        return items
            .Select(s => new ScoreDistributionItem
            {
                Score = s.Score ?? 0,
                Amount = s.Amount ?? 0,
                BarHeight = maxAmount > 0 ? (double)(s.Amount ?? 0) / maxAmount * 100 : 0,
            })
            .ToList();
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

    private static AniListUser MapUser(ViewerFullDto dto)
    {
        return new AniListUser
        {
            Id = dto.Id,
            Name = dto.Name ?? string.Empty,
            About = dto.About,
            AvatarLarge = dto.Avatar?.Large,
            AvatarMedium = dto.Avatar?.Medium,
            BannerImage = dto.BannerImage,
            SiteUrl = dto.SiteUrl,
            DonatorTier = dto.DonatorTier,
            DonatorBadge = dto.DonatorBadge,
            ScoreFormat = ParseScoreFormat(dto.MediaListOptions?.ScoreFormat),
            RowOrder = dto.MediaListOptions?.RowOrder,
            Options = new UserOptions
            {
                TitleLanguage = ParseTitleLanguage(dto.Options?.TitleLanguage),
                DisplayAdultContent = dto.Options?.DisplayAdultContent ?? false,
                AiringNotifications = dto.Options?.AiringNotifications ?? false,
                ProfileColor = dto.Options?.ProfileColor ?? string.Empty,
                Timezone = dto.Options?.Timezone,
                ActivityMergeTime = dto.Options?.ActivityMergeTime ?? 0,
                StaffNameLanguage = ParseStaffNameLanguage(dto.Options?.StaffNameLanguage),
                RestrictMessagesToFollowing = dto.Options?.RestrictMessagesToFollowing ?? false,
                NotificationOptions = dto.Options?.NotificationOptions?
                    .Select(n => new NotificationOption { Type = n.Type ?? string.Empty, Enabled = n.Enabled ?? false })
                    .ToList() ?? []
            },
            AnimeStatistics = new UserAnimeStatistics
            {
                Count = dto.Statistics?.Anime?.Count ?? 0,
                MeanScore = dto.Statistics?.Anime?.MeanScore ?? 0,
                MinutesWatched = dto.Statistics?.Anime?.MinutesWatched ?? 0,
                EpisodesWatched = dto.Statistics?.Anime?.EpisodesWatched ?? 0
            }
        };
    }

    private static UserTitleLanguage ParseTitleLanguage(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "ROMAJI" or "ROMAJI_STYLISED" => UserTitleLanguage.Romaji,
            "ENGLISH" or "ENGLISH_STYLISED" => UserTitleLanguage.English,
            "NATIVE" or "NATIVE_STYLISED" => UserTitleLanguage.Native,
            _ => UserTitleLanguage.Romaji
        };
    }

    private static string ToTitleLanguageString(UserTitleLanguage value)
    {
        return value switch
        {
            UserTitleLanguage.Romaji => "ROMAJI",
            UserTitleLanguage.English => "ENGLISH",
            UserTitleLanguage.Native => "NATIVE",
            _ => "ROMAJI"
        };
    }

    private static ScoreFormat ParseScoreFormat(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "POINT_100" => ScoreFormat.Point100,
            "POINT_10_DECIMAL" => ScoreFormat.Point10Decimal,
            "POINT_10" => ScoreFormat.Point10,
            "POINT_5" => ScoreFormat.Point5,
            "POINT_3" => ScoreFormat.Point3,
            _ => ScoreFormat.Point10
        };
    }

    private static string ToScoreFormatString(ScoreFormat value)
    {
        return value switch
        {
            ScoreFormat.Point100 => "POINT_100",
            ScoreFormat.Point10Decimal => "POINT_10_DECIMAL",
            ScoreFormat.Point10 => "POINT_10",
            ScoreFormat.Point5 => "POINT_5",
            ScoreFormat.Point3 => "POINT_3",
            _ => "POINT_10"
        };
    }

    private static UserStaffNameLanguage ParseStaffNameLanguage(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "ROMAJI_WESTERN" => UserStaffNameLanguage.RomajiWestern,
            "ROMAJI" => UserStaffNameLanguage.Romaji,
            "NATIVE" => UserStaffNameLanguage.Native,
            _ => UserStaffNameLanguage.RomajiWestern
        };
    }

    private static string ToStaffNameLanguageString(UserStaffNameLanguage value)
    {
        return value switch
        {
            UserStaffNameLanguage.RomajiWestern => "ROMAJI_WESTERN",
            UserStaffNameLanguage.Romaji => "ROMAJI",
            UserStaffNameLanguage.Native => "NATIVE",
            _ => "ROMAJI_WESTERN"
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

    private sealed class DeleteEntryData
    {
        public DeletedResult? DeleteMediaListEntry { get; set; }
    }

    private sealed class DeletedResult
    {
        public bool Deleted { get; set; }
    }

    private sealed class ViewerData
    {
        public ViewerDto? Viewer { get; set; }
    }

    private sealed class ViewerDto
    {
        public int Id { get; set; }
    }

    private sealed class ViewerFullData
    {
        public ViewerFullDto? Viewer { get; set; }
    }

    private sealed class UpdateUserData
    {
        public ViewerFullDto? UpdateUser { get; set; }
    }

    private sealed class ViewerFullDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? About { get; set; }
        public UserAvatarDto? Avatar { get; set; }
        public string? BannerImage { get; set; }
        public string? SiteUrl { get; set; }
        public int? DonatorTier { get; set; }
        public string? DonatorBadge { get; set; }
        public UserOptionsDto? Options { get; set; }
        public MediaListOptionsDto? MediaListOptions { get; set; }
        public UserStatisticTypesDto? Statistics { get; set; }
    }

    private sealed class UserAvatarDto
    {
        public string? Large { get; set; }
        public string? Medium { get; set; }
    }

    private sealed class UserOptionsDto
    {
        public string? TitleLanguage { get; set; }
        public bool? DisplayAdultContent { get; set; }
        public bool? AiringNotifications { get; set; }
        public string? ProfileColor { get; set; }
        public string? Timezone { get; set; }
        public int? ActivityMergeTime { get; set; }
        public string? StaffNameLanguage { get; set; }
        public bool? RestrictMessagesToFollowing { get; set; }
        public List<NotificationOptionDto>? NotificationOptions { get; set; }
    }

    private sealed class NotificationOptionDto
    {
        public string? Type { get; set; }
        public bool? Enabled { get; set; }
    }

    private sealed class MediaListOptionsDto
    {
        public string? ScoreFormat { get; set; }
        public string? RowOrder { get; set; }
    }

    private sealed class UserStatisticTypesDto
    {
        public UserStatisticsDto? Anime { get; set; }
    }

    private sealed class UserStatisticsDto
    {
        public int? Count { get; set; }
        public double? MeanScore { get; set; }
        public int? MinutesWatched { get; set; }
        public int? EpisodesWatched { get; set; }
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
        public MediaRelationConnectionDto? Relations { get; set; }
        public CharacterConnectionDto? Characters { get; set; }
        public RecommendationConnectionDto? Recommendations { get; set; }
        public MediaStatsDto? Stats { get; set; }
        public StaffConnectionDto? Staff { get; set; }
        public MediaListEntryDto? MediaListEntry { get; set; }
    }

    private sealed class StudioConnection
    {
        public List<Studio>? Nodes { get; set; }
    }

    private sealed class MediaRelationConnectionDto
    {
        public List<MediaRelationEdgeDto>? Edges { get; set; }
    }

    private sealed class MediaRelationEdgeDto
    {
        public string? RelationType { get; set; }
        public RelatedMediaDto? Node { get; set; }
    }

    private sealed class RelatedMediaDto
    {
        public int Id { get; set; }
        public MediaTitle? Title { get; set; }
        public string? Format { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public MediaCoverImage? CoverImage { get; set; }
        public int? AverageScore { get; set; }
    }

    private sealed class CharacterConnectionDto
    {
        public List<CharacterEdgeDto>? Edges { get; set; }
    }

    private sealed class CharacterEdgeDto
    {
        public CharacterNodeDto? Node { get; set; }
        public string? Role { get; set; }
        public List<VoiceActorDto>? VoiceActors { get; set; }
    }

    private sealed class CharacterNodeDto
    {
        public int Id { get; set; }
        public CharacterName? Name { get; set; }
        public CharacterImage? Image { get; set; }
    }

    private sealed class VoiceActorDto
    {
        public int Id { get; set; }
        public CharacterName? Name { get; set; }
        public CharacterImage? Image { get; set; }
        public string? Language { get; set; }
    }

    private sealed class RecommendationConnectionDto
    {
        public List<RecommendationNodeDto>? Nodes { get; set; }
    }

    private sealed class RecommendationNodeDto
    {
        public int? Rating { get; set; }
        public RelatedMediaDto? MediaRecommendation { get; set; }
    }

    private sealed class MediaStatsDto
    {
        public List<ScoreDistributionDto>? ScoreDistribution { get; set; }
        public List<StatusDistribution>? StatusDistribution { get; set; }
    }

    private sealed class ScoreDistributionDto
    {
        public int? Score { get; set; }
        public int? Amount { get; set; }
    }

    private sealed class StaffConnectionDto
    {
        public List<StaffEdgeDto>? Edges { get; set; }
    }

    private sealed class StaffEdgeDto
    {
        public StaffNodeDto? Node { get; set; }
        public string? Role { get; set; }
    }

    private sealed class StaffNodeDto
    {
        public int Id { get; set; }
        public CharacterName? Name { get; set; }
        public CharacterImage? Image { get; set; }
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
          isAdult
          nextAiringEpisode { episode airingAt timeUntilAiring }
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
    relations {
      edges {
        relationType(version: 2)
        node {
          id
          title { romaji english native }
          format
          type
          status
          coverImage { medium large }
        }
      }
    }
    characters(page: 1, perPage: 10, sort: [ROLE, RELEVANCE, ID]) {
      edges {
        node {
          id
          name { full native }
          image { medium large }
        }
        role
        voiceActors(sort: [RELEVANCE, ID]) {
          id
          name { full native }
          image { medium }
          language
        }
      }
    }
    recommendations(page: 1, perPage: 8, sort: [RATING_DESC]) {
      nodes {
        rating
        mediaRecommendation {
          id
          title { romaji english native }
          format
          coverImage { medium large }
          averageScore
        }
      }
    }
    stats {
      scoreDistribution { score amount }
      statusDistribution { status amount }
    }
    staff(page: 1, perPage: 10, sort: [RELEVANCE, ID]) {
      edges {
        node {
          id
          name { full native }
          image { medium }
        }
        role
      }
    }
    mediaListEntry {
      id
      mediaId
      status
      progress
      score
      repeat
      notes
      private
      hiddenFromStatusLists
      updatedAt
    }
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

    private const string DeleteEntryMutation = @"
mutation DeleteMediaListEntry($id: Int!) {
  DeleteMediaListEntry(id: $id) {
    deleted
  }
}";

    private const string ViewerFullQuery = @"
query ViewerFull {
  Viewer {
    id
    name
    about
    avatar { large medium }
    bannerImage
    siteUrl
    donatorTier
    donatorBadge
    options {
      titleLanguage
      displayAdultContent
      airingNotifications
      profileColor
      timezone
      activityMergeTime
      staffNameLanguage
      restrictMessagesToFollowing
      notificationOptions { type enabled }
    }
    mediaListOptions {
      scoreFormat
      rowOrder
    }
    statistics {
      anime {
        count
        meanScore
        minutesWatched
        episodesWatched
      }
    }
  }
}";

    private const string UpdateUserMutation = @"
mutation UpdateUser($titleLanguage: UserTitleLanguage, $displayAdultContent: Boolean, $airingNotifications: Boolean, $scoreFormat: ScoreFormat, $profileColor: String, $staffNameLanguage: UserStaffNameLanguage, $restrictMessagesToFollowing: Boolean, $activityMergeTime: Int, $notificationOptions: [NotificationOptionInput]) {
  UpdateUser(titleLanguage: $titleLanguage, displayAdultContent: $displayAdultContent, airingNotifications: $airingNotifications, scoreFormat: $scoreFormat, profileColor: $profileColor, staffNameLanguage: $staffNameLanguage, restrictMessagesToFollowing: $restrictMessagesToFollowing, activityMergeTime: $activityMergeTime, notificationOptions: $notificationOptions) {
    id
    name
    about
    avatar { large medium }
    bannerImage
    siteUrl
    donatorTier
    donatorBadge
    options {
      titleLanguage
      displayAdultContent
      airingNotifications
      profileColor
      timezone
      activityMergeTime
      staffNameLanguage
      restrictMessagesToFollowing
      notificationOptions { type enabled }
    }
    mediaListOptions {
      scoreFormat
      rowOrder
    }
    statistics {
      anime {
        count
        meanScore
        minutesWatched
        episodesWatched
      }
    }
  }
}";
}
