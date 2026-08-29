using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AniSprinkles.Utilities;

namespace AniSprinkles.Services;

/// <summary>
/// The airing worker's own AniList query, hand-rolled and deliberately separate from
/// <c>AniListClient</c>.
/// <para>
/// The duplication is the price of the worker's independence: it can run after a reboot before the
/// app has ever launched, so it cannot reach a DI-registered client, its auth handler, or its
/// caching decorator. The endpoint is public, so no token is involved either.
/// </para>
/// <para>
/// Lives in Core rather than beside the worker (#141) so the paging loop and — more importantly —
/// the failure classification can be tested. What it treats as a failure is load-bearing:
/// <see cref="AiringCheckRunner"/> leaves the checkpoint unadvanced only when this throws, so a
/// failure that returned an empty list instead would silently mark the window as checked and lose
/// every episode in it.
/// </para>
/// </summary>
public static class AiringScheduleFetcher
{
    public static readonly Uri GraphQlEndpoint = new("https://graphql.anilist.co");

    private const int PerPage = 50;

    /// <summary>Guards against a malformed <c>hasNextPage</c> spinning the worker forever.</summary>
    public const int MaxPages = 40;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public const string Query = """
        query AiringSchedule($mediaIds: [Int], $airingAfter: Int, $airingBefore: Int, $page: Int) {
          Page(page: $page, perPage: 50) {
            pageInfo { hasNextPage }
            airingSchedules(mediaId_in: $mediaIds, airingAt_greater: $airingAfter, airingAt_lesser: $airingBefore, sort: TIME) {
              id airingAt episode mediaId
              media { id title { romaji english native } coverImage { medium } }
            }
          }
        }
        """;

    /// <summary>
    /// Fetches everything that aired in the window for the given media, following pagination.
    /// <para>
    /// Synchronous because <c>Worker.DoWork</c> is, and it already runs on a background thread.
    /// </para>
    /// </summary>
    /// <param name="titleLanguage">
    /// Resolved by the caller rather than read here: the worker reads the raw preference because it
    /// cannot rely on <c>AppSettings.Load()</c> having run.
    /// </param>
    /// <exception cref="HttpRequestException">Any non-success status.</exception>
    /// <exception cref="InvalidOperationException">
    /// A GraphQL <c>errors</c> array, or a null <c>Page</c> — AniList returns both on HTTP 200.
    /// </exception>
    public static IReadOnlyList<AiringEntry> Fetch(
        HttpClient client,
        IReadOnlyList<int> mediaIds,
        long airingAfter,
        long airingBefore,
        UserTitleLanguage titleLanguage)
    {
        var results = new List<AiringEntry>();
        int page = 1;
        bool hasNextPage;

        do
        {
            var payload = new
            {
                query = Query,
                variables = new
                {
                    mediaIds,
                    airingAfter = (int)airingAfter,
                    airingBefore = (int)airingBefore,
                    page,
                },
                operationName = "AiringSchedule"
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload, WriteOptions), Encoding.UTF8, "application/json");

            using var response = client.PostAsync(GraphQlEndpoint, content).GetAwaiter().GetResult();

            // Throw rather than return partial results, so the caller keeps the window for retry.
            response.EnsureSuccessStatusCode();

            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var graphQl = JsonSerializer.Deserialize<GraphQlResponse>(json, ReadOptions);

            // AniList can return HTTP 200 with data=null and a populated errors array. Treat that as
            // a failed query so the time window is not silently skipped.
            if (graphQl?.Errors is { Count: > 0 } || graphQl?.Data?.Page is null)
            {
                throw new InvalidOperationException(
                    graphQl?.Errors?.FirstOrDefault()?.Message
                    ?? "AniList returned null data for AiringSchedule query");
            }

            foreach (var dto in graphQl.Data.Page.AiringSchedules ?? [])
            {
                results.Add(new AiringEntry
                {
                    MediaId = dto.MediaId,
                    Episode = dto.Episode,
                    MediaTitle = SelectTitle(dto.Media?.Title, titleLanguage),
                    CoverImageUrl = dto.Media?.CoverImage?.Medium,
                });
            }

            hasNextPage = graphQl.Data.Page.PageInfo?.HasNextPage == true;
            page++;
        }
        while (hasNextPage && page <= MaxPages);

        return results;
    }

    /// <summary>
    /// Through <see cref="TitleSelector"/>, so a notification cannot disagree with the title on the
    /// screen it links to.
    /// </summary>
    private static string SelectTitle(AiringTitleDto? title, UserTitleLanguage language)
        => title is null
            ? TitleSelector.UnknownTitle
            : TitleSelector.Select(language, title.Romaji, title.English, title.Native);

    // ── DTOs for the worker's own GraphQL parsing ───────────────────
    // Intentionally separate from AniListClient's, so this path depends on nothing DI-resolved.

    private sealed class GraphQlResponse
    {
        public ResponseData? Data { get; set; }
        public List<GraphQlError>? Errors { get; set; }
    }

    private sealed class GraphQlError
    {
        public string? Message { get; set; }
    }

    private sealed class ResponseData
    {
        public PageData? Page { get; set; }
    }

    private sealed class PageData
    {
        public PageInfoData? PageInfo { get; set; }
        public List<AiringScheduleDto>? AiringSchedules { get; set; }
    }

    private sealed class PageInfoData
    {
        public bool? HasNextPage { get; set; }
    }

    private sealed class AiringScheduleDto
    {
        public int MediaId { get; set; }
        public int Episode { get; set; }
        public AiringMediaDto? Media { get; set; }
    }

    private sealed class AiringMediaDto
    {
        public AiringTitleDto? Title { get; set; }
        public AiringCoverDto? CoverImage { get; set; }
    }

    private sealed class AiringTitleDto
    {
        public string? Romaji { get; set; }
        public string? English { get; set; }
        public string? Native { get; set; }
    }

    private sealed class AiringCoverDto
    {
        public string? Medium { get; set; }
    }
}
