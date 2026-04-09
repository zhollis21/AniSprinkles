using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Android.Content;
using AndroidX.Work;
using Bitmap = global::Android.Graphics.Bitmap;

namespace AniSprinkles.Platforms.Android;

/// <summary>
/// WorkManager <see cref="Worker"/> that polls AniList's public AiringSchedule API for
/// episodes that have aired since the last check, and posts local notifications.
/// Fully self-contained — makes its own HTTP requests without depending on MAUI DI,
/// so it works even if the app hasn't been launched since a device reboot.
/// </summary>
public class AiringCheckWorker : Worker
{
    private const string MediaIdsPrefKey = "airing_media_ids";
    private const string LastCheckPrefKey = "airing_last_check";
    private const string NotifiedPrefKey = "airing_notified";
    private const int StaleEntryDays = 7;

    private static readonly Uri GraphQlEndpoint = new("https://graphql.anilist.co");

    // Shared across all worker runs in this process. HttpClient is thread-safe and designed
    // to be reused. Timeout guards against hung requests stalling the worker thread indefinitely.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string AiringScheduleQuery = """
        query AiringSchedule($mediaIds: [Int], $airingAfter: Int, $airingBefore: Int, $page: Int) {
          Page(page: $page, perPage: 50) {
            pageInfo { hasNextPage }
            airingSchedules(mediaId_in: $mediaIds, airingAt_greater: $airingAfter, airingAt_lesser: $airingBefore, sort: TIME) {
              id airingAt episode mediaId
              media { id title { userPreferred } coverImage { medium } }
            }
          }
        }
        """;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(AiringCheckWorker))]
    public AiringCheckWorker(Context context, WorkerParameters workerParams)
        : base(context, workerParams)
    {
    }

    public override Result DoWork()
    {
        try
        {
            var mediaIds = ReadMediaIds();
            if (mediaIds.Count == 0)
            {
                return Result.InvokeSuccess()!;
            }

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long lastCheck = Preferences.Default.Get(LastCheckPrefKey, nowUnix - 1800); // default: 30 min ago

            // FetchAiringSchedule throws on any HTTP failure — if it throws, the catch below
            // skips the LastCheckPrefKey update so the time window is retried on the next run.
            var entries = FetchAiringSchedule(mediaIds, (int)lastCheck, (int)nowUnix);
            var notifiedSet = ReadNotifiedSet();
            bool changed = false;

            foreach (var entry in entries)
            {
                string key = $"{entry.MediaId}:{entry.Episode}";
                if (notifiedSet.ContainsKey(key))
                {
                    continue;
                }

                Bitmap? coverBitmap = null;
                if (!string.IsNullOrEmpty(entry.CoverImageUrl))
                {
                    coverBitmap = NotificationHelper.DownloadBitmap(entry.CoverImageUrl);
                }

                NotificationHelper.Show(ApplicationContext!, entry.MediaId, entry.MediaTitle, entry.Episode, coverBitmap);
                coverBitmap?.Dispose();

                notifiedSet[key] = nowUnix;
                changed = true;
            }

            // Only advance the checkpoint after a fully successful fetch — partial/failed
            // fetches throw before reaching here so the time window is not silently skipped.
            Preferences.Default.Set(LastCheckPrefKey, nowUnix);
            PruneAndSaveNotifiedSet(notifiedSet, nowUnix, changed);

            return Result.InvokeSuccess()!;
        }
        catch (Exception ex)
        {
            // Don't retry on transient errors; the next periodic run will try again.
            // Log to logcat so failures are diagnosable — the Worker has no DI/ILogger.
            global::Android.Util.Log.Error("AiringCheckWorker", $"DoWork failed: {ex.Message}");
            return Result.InvokeSuccess()!;
        }
    }

    // ── Self-contained AniList query ────────────────────────────────

    /// <summary>
    /// Queries AniList's public AiringSchedule API directly via HTTP.
    /// No auth token needed — this is a public endpoint.
    /// </summary>
    private static List<AiringEntry> FetchAiringSchedule(List<int> mediaIds, int airingAfter, int airingBefore)
    {
        var results = new List<AiringEntry>();
        int page = 1;
        bool hasNextPage;

        do
        {
            var payload = new
            {
                query = AiringScheduleQuery,
                variables = new { mediaIds, airingAfter, airingBefore, page },
                operationName = "AiringSchedule"
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload, JsonWriteOptions),
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = HttpClient.PostAsync(GraphQlEndpoint, content).GetAwaiter().GetResult();

            // Throw on non-success so DoWork's catch skips the LastCheckPrefKey update,
            // keeping the failed time window available for retry on the next run.
            response.EnsureSuccessStatusCode();

            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var graphQl = JsonSerializer.Deserialize<GraphQlResponse>(json, JsonReadOptions);

            // AniList can return HTTP 200 with data=null and a populated errors array.
            // Treat this as a failed query so the time window is not silently skipped.
            if (graphQl?.Errors is { Count: > 0 } || graphQl?.Data?.Page is null)
            {
                throw new InvalidOperationException(
                    graphQl?.Errors?.FirstOrDefault()?.Message ?? "AniList returned null data for AiringSchedule query");
            }

            if (graphQl.Data.Page.AiringSchedules is { } schedules)
            {
                foreach (var dto in schedules)
                {
                    results.Add(new AiringEntry
                    {
                        MediaId = dto.MediaId,
                        Episode = dto.Episode,
                        MediaTitle = dto.Media?.Title?.UserPreferred ?? string.Empty,
                        CoverImageUrl = dto.Media?.CoverImage?.Medium,
                    });
                }
            }

            hasNextPage = graphQl?.Data?.Page?.PageInfo?.HasNextPage == true;
            page++;
        }
        while (hasNextPage);

        return results;
    }

    // ── Preferences helpers ─────────────────────────────────────────

    private static List<int> ReadMediaIds()
    {
        string raw = Preferences.Default.Get(MediaIdsPrefKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var ids = new List<int>();
        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static Dictionary<string, long> ReadNotifiedSet()
    {
        string raw = Preferences.Default.Get(NotifiedPrefKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Dictionary<string, long>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, long>>(raw)
                ?? new Dictionary<string, long>();
        }
        catch
        {
            return new Dictionary<string, long>();
        }
    }

    private static void PruneAndSaveNotifiedSet(Dictionary<string, long> notifiedSet, long nowUnix, bool forceWrite)
    {
        long cutoff = nowUnix - (StaleEntryDays * 86400);
        var staleKeys = notifiedSet.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        foreach (string key in staleKeys)
        {
            notifiedSet.Remove(key);
        }

        // Write if new entries were added or stale entries were pruned
        if (forceWrite || staleKeys.Count > 0)
        {
            Preferences.Default.Set(NotifiedPrefKey, JsonSerializer.Serialize(notifiedSet));
        }
    }

    // ── Lightweight DTOs for the worker's own GraphQL parsing ───────
    // These are intentionally separate from the main app's AniListClient DTOs
    // so the worker has zero dependency on MAUI DI or shared services.

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
        public string? UserPreferred { get; set; }
    }

    private sealed class AiringCoverDto
    {
        public string? Medium { get; set; }
    }

    private record struct AiringEntry
    {
        public int MediaId { get; init; }
        public int Episode { get; init; }
        public string MediaTitle { get; init; }
        public string? CoverImageUrl { get; init; }
    }
}
