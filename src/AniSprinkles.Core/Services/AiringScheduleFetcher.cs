using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AniSprinkles.Utilities;
using Sentry;

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

    /// <summary>
    /// AniList rejects any request carrying neither a <c>Referer</c> nor an <c>Authorization</c>
    /// header with HTTP 403 (#160). This fetcher has no token by design — that independence is the
    /// whole reason it exists — so the Referer is the only thing keeping the worker able to reach
    /// AniList at all. Duplicated from <c>AniListClient</c> for the same reason the query is.
    /// </summary>
    public static readonly Uri GraphQlReferer = new("https://anilist.co/");

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
    public static AiringScheduleResult Fetch(
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
                    // checked, because AniList's airingAt arguments are Int and unix seconds pass
                    // int.MaxValue in January 2038. An unchecked cast would wrap negative, quietly
                    // query a nonsense window, and let the caller advance the checkpoint past
                    // episodes it never saw. Throwing keeps the window for the next run instead —
                    // the same bargain every other failure in this method makes.
                    airingAfter = checked((int)airingAfter),
                    airingBefore = checked((int)airingBefore),
                    page,
                },
                operationName = "AiringSchedule"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, WriteOptions), Encoding.UTF8, "application/json"),
            };
            request.Headers.Referrer = GraphQlReferer;

            // Blocking-async rather than the synchronous HttpClient.Send: the sync path bottoms out
            // in HttpMessageHandler.Send, which handlers are not obliged to implement, and neither
            // the test fake nor the platform handler does.
            using var response = client.SendAsync(request).GetAwaiter().GetResult();

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
                    AiringAt = dto.AiringAt,
                    MediaTitle = SelectTitle(dto.Media?.Title, titleLanguage),
                    CoverImageUrl = dto.Media?.CoverImage?.Medium,
                });
            }

            hasNextPage = graphQl.Data.Page.PageInfo?.HasNextPage == true;
            page++;
        }
        while (hasNextPage && page <= MaxPages);

        // Still claiming another page at the bound means the window was not read to the end. The
        // caller is told so it can hold the checkpoint back rather than skipping what we never saw.
        bool truncated = hasNextPage;
        if (truncated)
        {
            // Worth an event rather than just a log line: reaching 2000 entries in one window is not
            // something a personal list does, so in practice this means a hasNextPage that never
            // goes false — a server-side or parsing fault we would otherwise never hear about.
            SentrySdk.CaptureMessage(
                $"AiringSchedule paging hit the {MaxPages}-page bound with hasNextPage still true "
                + $"({results.Count} entries, {mediaIds.Count} media ids, window {airingBefore - airingAfter}s)",
                SentryLevel.Error);
        }

        return new AiringScheduleResult(results, truncated);
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
        public int AiringAt { get; set; }
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
