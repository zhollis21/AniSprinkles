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
        string? english = "Attack on Titan", string? cover = "https://img/cover.jpg", long airingAt = 1_700_001_000)
        => $$"""
            {
              "mediaId": {{mediaId}}, "episode": {{episode}}, "airingAt": {{airingAt}},
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
        => FetchResult(handler, language, mediaIds).Entries;

    private static AiringScheduleResult FetchResult(
        ScriptedGraphQlHandler handler,
        UserTitleLanguage language = UserTitleLanguage.Romaji,
        params int[] mediaIds)
    {
        using var client = ClientFor(handler);
        return AiringScheduleFetcher.Fetch(
            client, mediaIds.Length == 0 ? [21] : mediaIds, After, Before, language);
    }

    // ── The request ─────────────────────────────────────────────────

    [Test]
    public void TheWindowAndMediaIds_AreSentAsVariables()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));

        Fetch(handler, UserTitleLanguage.Romaji, 21, 16498);

        var request = handler.Last;
        Assert.That(request.OperationName, Is.EqualTo("AiringSchedule"));
        Assert.That(request.IntVariable("airingAfter"), Is.EqualTo((int)After));
        Assert.That(request.IntVariable("airingBefore"), Is.EqualTo((int)Before));
        Assert.That(request.Variable("mediaIds").EnumerateArray().Select(e => e.GetInt32()), Is.EqualTo([21, 16498]));
    }

    [Test]
    public void TheAniListReferer_IsSent()
    {
        // With no token by design (see below), the Referer is the *only* thing that gets this
        // request past AniList, which 403s anything carrying neither — behind a body claiming the
        // API is temporarily disabled (#160). This fetcher deliberately bypasses AniListClient, so
        // it does not inherit the header set there and has to send its own.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));

        Fetch(handler);

        Assert.That(handler.Last.Referrer?.ToString(), Is.EqualTo("https://anilist.co/"));
    }

    [Test]
    public void NoAuthorizationHeader_IsSent()
    {
        // The AiringSchedule endpoint is public. The worker has no access to the token store, and
        // must not grow one — that is what lets it run before the app has been launched.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));

        Fetch(handler);

        Assert.That(handler.Last.AuthScheme, Is.Null);
        Assert.That(handler.Last.BearerToken, Is.Null);
    }

    // ── Mapping ─────────────────────────────────────────────────────

    [Test]
    public void EachSchedule_BecomesAnEntry()
    {
        var handler = new ScriptedGraphQlHandler(
            _ => ScriptedGraphQlHandler.Data(Page(false, Schedule(21, 1050), Schedule(16498, 25))));

        var entries = Fetch(handler);

        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].MediaId, Is.EqualTo(21));
        Assert.That(entries[0].Episode, Is.EqualTo(1050));
        Assert.That(entries[0].CoverImageUrl, Is.EqualTo("https://img/cover.jpg"));
        Assert.That(entries[1].MediaId, Is.EqualTo(16498));
    }

    [TestCase(UserTitleLanguage.Romaji, "Shingeki no Kyojin")]
    [TestCase(UserTitleLanguage.English, "Attack on Titan")]
    [TestCase(UserTitleLanguage.Native, "進撃の巨人")]
    public void TheTitle_FollowsTheRequestedLanguage(UserTitleLanguage language, string expected)
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false, Schedule(21, 1050))));

        var entries = Fetch(handler, language);

        Assert.That(entries[0].MediaTitle, Is.EqualTo(expected));
    }

    [Test]
    public void AMissingPreferredTitle_FallsBackRatherThanBlanking()
    {
        var handler = new ScriptedGraphQlHandler(
            _ => ScriptedGraphQlHandler.Data(Page(false, Schedule(21, 1050, english: null))));

        Assert.That(Fetch(handler, UserTitleLanguage.English)[0].MediaTitle, Is.EqualTo("Shingeki no Kyojin"));
    }

    [Test]
    public void AScheduleWithNoMedia_StillYieldsAnEntry()
    {
        // Better a notification titled "Unknown Title" than a silently dropped episode.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(
            Page(false, """{ "mediaId": 21, "episode": 1050, "media": null }""")));

        var entries = Fetch(handler);

        Assert.That(entries, Has.Exactly(1).Items);
        Assert.That(entries[0].MediaTitle, Is.EqualTo(TitleSelector.UnknownTitle));
        Assert.That(entries[0].CoverImageUrl, Is.Null);
    }

    [Test]
    public void AnEmptySchedule_IsNotAFailure()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));

        Assert.That(Fetch(handler), Is.Empty);
    }

    [Test]
    public void ANullSchedulesArray_IsNotAFailure()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(
            """{ "Page": { "pageInfo": { "hasNextPage": false }, "airingSchedules": null } }"""));

        Assert.That(Fetch(handler), Is.Empty);
    }

    // ── Paging ──────────────────────────────────────────────────────

    [Test]
    public void PagesAreFollowed_AndAccumulate()
    {
        var handler = new ScriptedGraphQlHandler(request => request.IntVariable("page") switch
        {
            1 => ScriptedGraphQlHandler.Data(Page(true, Schedule(21, 1050))),
            2 => ScriptedGraphQlHandler.Data(Page(true, Schedule(16498, 25))),
            _ => ScriptedGraphQlHandler.Data(Page(false, Schedule(101922, 12))),
        });

        var entries = Fetch(handler);

        Assert.That(handler.CallCount, Is.EqualTo(3));
        Assert.That(entries.Select(e => e.MediaId), Is.EqualTo([21, 16498, 101922]));
    }

    [Test]
    public void OnlyOnePage_IsRequestedWhenThereIsNoNext()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false, Schedule(21, 1050))));

        Fetch(handler);

        Assert.That(handler.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void AMissingPageInfo_StopsRatherThanLooping()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(
            $$"""{ "Page": { "airingSchedules": [{{Schedule(21, 1050)}}] } }"""));

        Fetch(handler);

        Assert.That(handler.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void AnAlwaysTruePageInfo_IsBounded_AndReportedAsTruncated()
    {
        // A server that always claims another page would otherwise spin the worker thread forever.
        // Stopping is not enough on its own: the caller has to know the window was not read to the
        // end, or it advances the checkpoint past episodes that were never fetched.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(true, Schedule(21, 1050))));

        var result = FetchResult(handler);

        Assert.That(handler.CallCount, Is.EqualTo(AiringScheduleFetcher.MaxPages));
        Assert.That(result.Truncated, Is.True);
        Assert.That(result.Entries, Is.Not.Empty);
    }

    [Test]
    public void AFetchThatReachesTheEnd_IsNotTruncated()
    {
        var handler = new ScriptedGraphQlHandler(request => request.IntVariable("page") == 1
            ? ScriptedGraphQlHandler.Data(Page(true, Schedule(21, 1050)))
            : ScriptedGraphQlHandler.Data(Page(false, Schedule(16498, 25))));

        Assert.That(FetchResult(handler).Truncated, Is.False);
    }

    [Test]
    public void TheAiringTime_IsMapped()
    {
        // Carried so a truncated fetch can advance the checkpoint to what it actually read.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(
            Page(false, Schedule(21, 1050, airingAt: 1_700_001_234))));

        Assert.That(Fetch(handler)[0].AiringAt, Is.EqualTo(1_700_001_234));
    }

    // ── Failure classification ──────────────────────────────────────

    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.BadRequest)]
    public void ANonSuccessStatus_Throws(HttpStatusCode status)
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Raw(status, "{}"));

        Assert.Throws<HttpRequestException>(() => Fetch(handler));
    }

    [Test]
    public void GraphQlErrorsOnHttp200_Throw()
    {
        // The case that motivates all of this: AniList reports failures with a 200 and an errors
        // array. Treating that as an empty result would advance the checkpoint past a window that
        // was never actually checked.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.GraphQlError("Too Many Requests"));

        var ex = Assert.Throws<InvalidOperationException>(() => Fetch(handler));
        Assert.That(ex.Message, Does.Contain("Too Many Requests"));
    }

    [Test]
    public void ANullPageOnHttp200_Throws()
    {
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data("""{ "Page": null }"""));

        Assert.Throws<InvalidOperationException>(() => Fetch(handler));
    }

    [Test]
    public void AWindowPastTheInt32Limit_ThrowsRatherThanWrappingNegative()
    {
        // AniList's airingAt arguments are Int, and unix seconds pass int.MaxValue in January 2038.
        // An unchecked cast would wrap negative and query a nonsense window, which looks like
        // success — so the caller would advance the checkpoint past episodes it never saw.
        var handler = new ScriptedGraphQlHandler(_ => ScriptedGraphQlHandler.Data(Page(false)));
        using var client = ClientFor(handler);

        Assert.Throws<OverflowException>(() => AiringScheduleFetcher.Fetch(
            client, [21], (long)int.MaxValue + 1, (long)int.MaxValue + 3600, UserTitleLanguage.Romaji));

        Assert.That(handler.CallCount, Is.EqualTo(0));
    }

    [Test]
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
