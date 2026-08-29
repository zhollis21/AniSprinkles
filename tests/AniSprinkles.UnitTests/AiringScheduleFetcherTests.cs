using System.Net;
using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #141. The airing worker's hand-rolled AniList query. It used to sit in the MAUI app project, so
/// its paging loop and — more importantly — its failure classification were unreachable from here.
/// That classification is load-bearing: <c>AiringCheckRunner</c> only preserves the check window
/// when this throws, so anything it mistakes for success loses every episode in that window.
/// </summary>
public class AiringScheduleFetcherTests
{
    private const long After = 1_700_000_000;
    private const long Before = 1_700_003_600;

    private static HttpClient ClientFor(ScriptedGraphQlHandler handler) => new(handler);

    private static string Schedule(int mediaId, int episode, string romaji = "Shingeki no Kyojin",
        string? english = "Attack on Titan", string? cover = "https://img/cover.jpg")
        => $$"""
            {
              "mediaId": {{mediaId}}, "episode": {{episode}},
              "media": {
                "title": { "romaji": "{{romaji}}", "english": {{(english is null ? "null" : $"\"{english}\"")}}, "native": "進撃の巨人" },
                "coverImage": { "medium": {{(cover is null ? "null" : $"\"{cover}\"")}} }
              }
            }
            """;

    private static string Page(bool hasNextPage, params string[] schedules)
        => $$"""{ "Page": { "pageInfo": { "hasNextPage": {{(hasNextPage ? "true" : "false")}} }, "airingSchedules": [{{string.Join(",", schedules)}}] } }""";

    private static IReadOnlyList<AiringEntry> Fetch(
        ScriptedGraphQlHandler handler,
        UserTitleLanguage language = UserTitleLanguage.Romaji,
        params int[] mediaIds)
    {
        using var client = ClientFor(handler);
        return AiringScheduleFetcher.Fetch(
            client, mediaIds.Length == 0 ? [21] : mediaIds, After, Before, language);
    }

    // ── The request ─────────────────────────────────────────────────

    [Fact]
    public void TheWindowAndMediaIds_AreSentAsVariables()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));

        Fetch(handler, UserTitleLanguage.Romaji, 21, 16498);

        var request = handler.Last;
        Assert.Equal("AiringSchedule", request.OperationName);
        Assert.Equal((int)After, request.IntVariable("airingAfter"));
        Assert.Equal((int)Before, request.IntVariable("airingBefore"));
        Assert.Equal([21, 16498], request.Variable("mediaIds").EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public void NoAuthorizationHeader_IsSent()
    {
        // The AiringSchedule endpoint is public. The worker has no access to the token store, and
        // must not grow one — that is what lets it run before the app has been launched.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));

        Fetch(handler);

        Assert.Null(handler.Last.AuthScheme);
        Assert.Null(handler.Last.BearerToken);
    }

    // ── Mapping ─────────────────────────────────────────────────────

    [Fact]
    public void EachSchedule_BecomesAnEntry()
    {
        var handler = new ScriptedGraphQlHandler(
            _ => ScriptedGraphQlHandler.Data(Page(false, Schedule(21, 1050), Schedule(16498, 25))));

        var entries = Fetch(handler);

        Assert.Equal(2, entries.Count);
        Assert.Equal(21, entries[0].MediaId);
        Assert.Equal(1050, entries[0].Episode);
        Assert.Equal("https://img/cover.jpg", entries[0].CoverImageUrl);
        Assert.Equal(16498, entries[1].MediaId);
    }

    [Theory]
    [InlineData(UserTitleLanguage.Romaji, "Shingeki no Kyojin")]
    [InlineData(UserTitleLanguage.English, "Attack on Titan")]
    [InlineData(UserTitleLanguage.Native, "進撃の巨人")]
    public void TheTitle_FollowsTheRequestedLanguage(UserTitleLanguage language, string expected)
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false, Schedule(21, 1050))));

        var entries = Fetch(handler, language);

        Assert.Equal(expected, entries[0].MediaTitle);
    }

    [Fact]
    public void AMissingPreferredTitle_FallsBackRatherThanBlanking()
    {
        var handler = new ScriptedGraphQlHandler(
            _ => ScriptedGraphQlHandler.Data(Page(false, Schedule(21, 1050, english: null))));

        Assert.Equal("Shingeki no Kyojin", Fetch(handler, UserTitleLanguage.English)[0].MediaTitle);
    }

    [Fact]
    public void AScheduleWithNoMedia_StillYieldsAnEntry()
    {
        // Better a notification titled "Unknown Title" than a silently dropped episode.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(
            Page(false, """{ "mediaId": 21, "episode": 1050, "media": null }""")));

        var entries = Fetch(handler);

        Assert.Single(entries);
        Assert.Equal(TitleSelector.UnknownTitle, entries[0].MediaTitle);
        Assert.Null(entries[0].CoverImageUrl);
    }

    [Fact]
    public void AnEmptySchedule_IsNotAFailure()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));

        Assert.Empty(Fetch(handler));
    }

    [Fact]
    public void ANullSchedulesArray_IsNotAFailure()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(
            """{ "Page": { "pageInfo": { "hasNextPage": false }, "airingSchedules": null } }"""));

        Assert.Empty(Fetch(handler));
    }

    // ── Paging ──────────────────────────────────────────────────────

    [Fact]
    public void PagesAreFollowed_AndAccumulate()
    {
        var handler = new ScriptedGraphQlHandler(request => request.IntVariable("page") switch
        {
            1 => ScriptedGraphQlHandler.Data(Page(true, Schedule(21, 1050))),
            2 => ScriptedGraphQlHandler.Data(Page(true, Schedule(16498, 25))),
            _ => ScriptedGraphQlHandler.Data(Page(false, Schedule(101922, 12))),
        });

        var entries = Fetch(handler);

        Assert.Equal(3, handler.CallCount);
        Assert.Equal([21, 16498, 101922], entries.Select(e => e.MediaId));
    }

    [Fact]
    public void OnlyOnePage_IsRequestedWhenThereIsNoNext()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false, Schedule(21, 1050))));

        Fetch(handler);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void AMissingPageInfo_StopsRatherThanLooping()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(
            $$"""{ "Page": { "airingSchedules": [{{Schedule(21, 1050)}}] } }"""));

        Fetch(handler);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void AnAlwaysTruePageInfo_IsBounded()
    {
        // A server that always claims another page would otherwise spin the worker thread forever.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(true, Schedule(21, 1050))));

        Fetch(handler);

        Assert.Equal(AiringScheduleFetcher.MaxPages, handler.CallCount);
    }

    // ── Failure classification ──────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public void ANonSuccessStatus_Throws(HttpStatusCode status)
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Raw(status, "{}"));

        Assert.Throws<HttpRequestException>(() => Fetch(handler));
    }

    [Fact]
    public void GraphQlErrorsOnHttp200_Throw()
    {
        // The case that motivates all of this: AniList reports failures with a 200 and an errors
        // array. Treating that as an empty result would advance the checkpoint past a window that
        // was never actually checked.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.GraphQlError("Too Many Requests"));

        var ex = Assert.Throws<InvalidOperationException>(() => Fetch(handler));
        Assert.Contains("Too Many Requests", ex.Message);
    }

    [Fact]
    public void ANullPageOnHttp200_Throws()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data("""{ "Page": null }"""));

        Assert.Throws<InvalidOperationException>(() => Fetch(handler));
    }

    [Fact]
    public void AWindowPastTheInt32Limit_ThrowsRatherThanWrappingNegative()
    {
        // AniList's airingAt arguments are Int, and unix seconds pass int.MaxValue in January 2038.
        // An unchecked cast would wrap negative and query a nonsense window, which looks like
        // success — so the caller would advance the checkpoint past episodes it never saw.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));
        using var client = ClientFor(handler);

        Assert.Throws<OverflowException>(() => AiringScheduleFetcher.Fetch(
            client, [21], (long)int.MaxValue + 1, (long)int.MaxValue + 3600, UserTitleLanguage.Romaji));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void AFailureOnALaterPage_ThrowsRatherThanReturningWhatItHad()
    {
        // Returning page 1's entries would look like success to the runner, which would then advance
        // the checkpoint past the episodes on page 2 that were never fetched.
        var handler = new ScriptedGraphQlHandler(request => request.IntVariable("page") == 1
            ? ScriptedGraphQlHandler.Data(Page(true, Schedule(21, 1050)))
            : ScriptedGraphQlHandler.Raw(HttpStatusCode.ServiceUnavailable, "{}"));

        Assert.Throws<HttpRequestException>(() => Fetch(handler));
    }
}
