using System.Net;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 2, the per-operation half of <see cref="AniListClient"/>: what each call puts in its
/// <c>variables</c> and what it makes of the response. The shared envelope, error handling and retry
/// are covered once in <see cref="AniListClientTests"/> rather than repeated per operation.
/// <para>
/// Deliberately risk-ordered rather than exhaustive across all two dozen operations — the mutations
/// (which can corrupt the user's actual list), the paged readers (where a dropped <c>pageInfo</c>
/// silently ends paging), and the viewer/settings round trip. The read-only detail-page pagers share
/// the same helpers as the pagers covered here.
/// </para>
/// </summary>
public class AniListClientOperationsTests
{
    // ── BrowseAnime ──────────────────────────────────────────────────

    [Test]
    public async Task BrowseAnimePage_SendsItsPagingAndFilters()
    {
        var harness = new Harness().Returns("BrowseAnime", EmptyPage);

        await harness.Client.BrowseAnimePageAsync(
            "SCORE_DESC", status: "RELEASING", season: "WINTER", seasonYear: 2026,
            isAdult: false, format: "MOVIE", page: 3, perPage: 25,
            cancellationToken: TestContext.CurrentContext.CancellationToken);

        var request = harness.Handler.Last;
        Assert.That(request.IntVariable("page"), Is.EqualTo(3));
        Assert.That(request.IntVariable("perPage"), Is.EqualTo(25));
        Assert.That(request.StringVariable("status"), Is.EqualTo("RELEASING"));
        Assert.That(request.StringVariable("season"), Is.EqualTo("WINTER"));
        Assert.That(request.IntVariable("seasonYear"), Is.EqualTo(2026));
        Assert.That(request.BoolVariable("isAdult"), Is.False);
        Assert.That(request.StringVariable("format"), Is.EqualTo("MOVIE"));
    }

    [Test]
    public async Task BrowseAnimePage_AppendsAnIdTiebreakerToTheSort()
    {
        // Without a stable secondary key, two media with equal popularity can swap between pages —
        // which shows up as a duplicate card and a missing one after Load More.
        var harness = new Harness().Returns("BrowseAnime", EmptyPage);

        await harness.Client.BrowseAnimePageAsync(
            "POPULARITY_DESC", cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(harness.Handler.Last.StringArrayVariable("sort"), Is.EqualTo(["POPULARITY_DESC", "ID"]));
    }

    [TestCase("ID")]
    [TestCase("ID_DESC")]
    public async Task BrowseAnimePage_DoesNotTiebreakASortThatIsAlreadyById(string sort)
    {
        var harness = new Harness().Returns("BrowseAnime", EmptyPage);

        await harness.Client.BrowseAnimePageAsync(
            sort, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(harness.Handler.Last.StringArrayVariable("sort"), Is.EqualTo([sort]));
    }

    [Test]
    public async Task BrowseAnimePage_DeserializesTheItemsAndThePagingCursor()
    {
        var harness = new Harness().Returns("BrowseAnime", """
            {"Page":{
                "pageInfo":{"currentPage":2,"hasNextPage":true},
                "media":[
                    {"id":5,"type":"ANIME","popularity":900,"title":{"romaji":"Frieren"}},
                    {"id":6,"type":"ANIME","popularity":800,"title":{"romaji":"Bocchi"}}
                ]}}
            """);

        var (items, pageInfo) = await harness.Client.BrowseAnimePageAsync(
            "POPULARITY_DESC", cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(items.Select(i => i.Node!.Id), Is.EqualTo([5, 6]));

        var first = items[0].Node;
        Assert.That(first, Is.Not.Null);
        Assert.That(first.Title, Is.Not.Null);
        Assert.That(first.Title.Romaji, Is.EqualTo("Frieren"));

        Assert.That(pageInfo, Is.Not.Null);
        Assert.That(pageInfo.CurrentPage, Is.EqualTo(2));
        Assert.That(pageInfo.HasNextPage, Is.True);
    }

    [Test]
    public async Task BrowseAnimePage_WithNoPageAtAll_ComesBackEmptyRatherThanThrowing()
    {
        // An empty final page is a normal end-of-list, not an error to surface.
        var harness = new Harness().Returns("BrowseAnime", """{"Page":null}""");

        var (items, pageInfo) = await harness.Client.BrowseAnimePageAsync(
            "POPULARITY_DESC", cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(items, Is.Empty);
        Assert.That(pageInfo, Is.Null);
    }

    // ── Media details ────────────────────────────────────────────────

    [Test]
    public async Task GetMedia_DeserializesTheMediaAndTheViewersOwnEntry()
    {
        var harness = new Harness().Returns("Media", """
            {"Media":{
                "id":9,"type":"ANIME","episodes":12,"weightedScore":88,
                "title":{"romaji":"Frieren","english":"Frieren"},
                "mediaListEntry":{"id":555,"status":"CURRENT","progress":4,"score":9.0}
            }}
            """);

        var (media, entry) = await harness.Client.GetMediaAsync(9, TestContext.CurrentContext.CancellationToken);

        Assert.That(media, Is.Not.Null);
        Assert.That(entry, Is.Not.Null);
        Assert.That(media.Id, Is.EqualTo(9));
        Assert.That(media.Episodes, Is.EqualTo(12));
        // Asserted rather than merely present in the payload: the response key moved with #163, and
        // a key that no longer binds deserializes to null without failing anything.
        Assert.That(media.WeightedScore, Is.EqualTo(88));
        Assert.That(entry.Id, Is.EqualTo(555));
        Assert.That(entry.Status, Is.EqualTo(MediaListStatus.Current));
        Assert.That(entry.Progress, Is.EqualTo(4));
    }

    [Test]
    public async Task GetMedia_WhenTheIdIsUnknown_ReturnsNothingRatherThanThrowing()
    {
        // The details page turns this into its own not-found state.
        var harness = new Harness().Returns("Media", """{"Media":null}""");

        var (media, entry) = await harness.Client.GetMediaAsync(9, TestContext.CurrentContext.CancellationToken);

        Assert.That(media, Is.Null);
        Assert.That(entry, Is.Null);
    }

    [Test]
    public async Task GetMedia_ForMediaNotOnTheViewersList_HasNoEntry()
    {
        var harness = new Harness().Returns("Media", """{"Media":{"id":9,"type":"ANIME"}}""");

        var (media, entry) = await harness.Client.GetMediaAsync(9, TestContext.CurrentContext.CancellationToken);

        Assert.That(media, Is.Not.Null);
        Assert.That(entry, Is.Null);
    }

    // ── Saving a list entry ──────────────────────────────────────────

    [Test]
    public async Task SaveMediaListEntry_SendsEveryEditableField()
    {
        // A field silently dropped here is data loss the user only notices later, on another device.
        var harness = new Harness().Returns("SaveMediaListEntry", """{"SaveMediaListEntry":{"id":1}}""");

        await harness.Client.SaveMediaListEntryAsync(
            new MediaListEntry
            {
                Id = 1,
                MediaId = 42,
                Status = MediaListStatus.Completed,
                Progress = 12,
                ProgressVolumes = 3,
                Score = 8.5,
                Repeat = 2,
                Notes = "rewatch",
                Private = true,
                HiddenFromStatusLists = true,
            },
            TestContext.CurrentContext.CancellationToken);

        var request = harness.Handler.Last;
        Assert.That(request.IntVariable("mediaId"), Is.EqualTo(42));
        Assert.That(request.StringVariable("status"), Is.EqualTo("COMPLETED"));
        Assert.That(request.IntVariable("progress"), Is.EqualTo(12));
        // Manga volumes are a second, independent counter (#12); omitting it silently discards
        // whatever the reader had set on AniList.
        Assert.That(request.IntVariable("progressVolumes"), Is.EqualTo(3));
        Assert.That(request.Variable("score").GetDouble(), Is.EqualTo(8.5));
        Assert.That(request.IntVariable("repeat"), Is.EqualTo(2));
        Assert.That(request.StringVariable("notes"), Is.EqualTo("rewatch"));
        Assert.That(request.BoolVariable("private"), Is.True);
        Assert.That(request.BoolVariable("hiddenFromStatusLists"), Is.True);
    }

    [TestCase(MediaListStatus.Current, "CURRENT")]
    [TestCase(MediaListStatus.Planning, "PLANNING")]
    [TestCase(MediaListStatus.Completed, "COMPLETED")]
    [TestCase(MediaListStatus.Dropped, "DROPPED")]
    [TestCase(MediaListStatus.Paused, "PAUSED")]
    [TestCase(MediaListStatus.Repeating, "REPEATING")]
    public async Task SaveMediaListEntry_SendsAniListsSpellingOfEachStatus(
        MediaListStatus status, string expected)
    {
        var harness = new Harness().Returns("SaveMediaListEntry", """{"SaveMediaListEntry":{"id":1}}""");

        await harness.Client.SaveMediaListEntryAsync(
            new MediaListEntry { MediaId = 1, Status = status },
            TestContext.CurrentContext.CancellationToken);

        Assert.That(harness.Handler.Last.StringVariable("status"), Is.EqualTo(expected));
    }

    [Test]
    public async Task SaveMediaListEntry_ReturnsTheServersVersionOfTheEntry()
    {
        // Add-to-list adopts the server-assigned id here; losing it orphans every later edit.
        var harness = new Harness().Returns("SaveMediaListEntry", """
            {"SaveMediaListEntry":{"id":777,"mediaId":42,"status":"CURRENT","progress":3,"updatedAt":86400}}
            """);

        var saved = await harness.Client.SaveMediaListEntryAsync(
            new MediaListEntry { MediaId = 42 }, TestContext.CurrentContext.CancellationToken);

        Assert.That(saved, Is.Not.Null);
        Assert.That(saved.Id, Is.EqualTo(777));
        Assert.That(saved.MediaId, Is.EqualTo(42));
        Assert.That(saved.Progress, Is.EqualTo(3));
        Assert.That(saved.UpdatedAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(86400)));
    }

    [Test]
    public async Task SaveMediaListEntry_WhenTheServerReturnsNoEntry_ReturnsNull()
    {
        var harness = new Harness().Returns("SaveMediaListEntry", """{"SaveMediaListEntry":null}""");

        var saved = await harness.Client.SaveMediaListEntryAsync(
            new MediaListEntry { MediaId = 42 }, TestContext.CurrentContext.CancellationToken);

        Assert.That(saved, Is.Null);
    }

    // ── Deleting a list entry ────────────────────────────────────────

    [TestCase(true)]
    [TestCase(false)]
    public async Task DeleteMediaListEntry_ReportsWhatTheServerSaid(bool deleted)
    {
        var harness = new Harness().Returns(
            "DeleteMediaListEntry",
            "{\"DeleteMediaListEntry\":{\"deleted\":" + (deleted ? "true" : "false") + "}}");

        var result = await harness.Client.DeleteMediaListEntryAsync(9, TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.EqualTo(deleted));
        Assert.That(harness.Handler.Last.IntVariable("id"), Is.EqualTo(9));
    }

    [Test]
    public async Task DeleteMediaListEntry_WithNoResultAtAll_IsTreatedAsNotDeleted()
    {
        // Reporting success here would remove the row locally while it still exists upstream.
        var harness = new Harness().Returns("DeleteMediaListEntry", """{"DeleteMediaListEntry":null}""");

        Assert.That(await harness.Client.DeleteMediaListEntryAsync(9, TestContext.CurrentContext.CancellationToken), Is.False);
    }

    // ── Favourites ───────────────────────────────────────────────────

    [TestCase(FavouriteKind.Anime, "animeId")]
    [TestCase(FavouriteKind.Manga, "mangaId")]
    [TestCase(FavouriteKind.Character, "characterId")]
    [TestCase(FavouriteKind.Staff, "staffId")]
    [TestCase(FavouriteKind.Studio, "studioId")]
    public async Task ToggleFavourite_SendsOnlyTheIdBelongingToItsKind(FavouriteKind kind, string expectedVariable)
    {
        // Every other id must be omitted rather than null — sending two would favourite two things.
        var harness = new Harness().Returns("ToggleFavourite", """{"ToggleFavourite":{}}""");

        await harness.Client.ToggleFavouriteAsync(kind, 31, TestContext.CurrentContext.CancellationToken);

        var request = harness.Handler.Last;
        Assert.That(request.IntVariable(expectedVariable), Is.EqualTo(31));
        foreach (var other in new[] { "animeId", "mangaId", "characterId", "staffId", "studioId" }
                     .Where(v => v != expectedVariable))
        {
            Assert.That(request.HasVariable(other), Is.False);
        }
    }

    [Test]
    public async Task ToggleFavourite_SucceedsOnAnEmptyResponseBody()
    {
        // Success is signalled by the absence of errors; the selection set exists only to make the
        // mutation valid.
        var harness = new Harness().Returns("ToggleFavourite", """{"ToggleFavourite":null}""");

        Assert.That(await harness.Client.ToggleFavouriteAsync(
            FavouriteKind.Anime, 31, TestContext.CurrentContext.CancellationToken), Is.True);
    }

    // ── The viewer ───────────────────────────────────────────────────

    [Test]
    public async Task GetViewer_DeserializesTheProfileAndTheDisplayPreferences()
    {
        var harness = new Harness().Returns("ViewerFull", """
            {"Viewer":{
                "id":11,"name":"zhollis","siteUrl":"https://anilist.co/user/zhollis",
                "avatar":{"large":"https://img/large.png"},
                "options":{"titleLanguage":"ENGLISH","displayAdultContent":true,"staffNameLanguage":"NATIVE"},
                "mediaListOptions":{"scoreFormat":"POINT_10_DECIMAL","animeList":{"sectionOrder":["Watching","Completed"]},"mangaList":{"sectionOrder":["Reading","Completed"]}},
                "statistics":{
                    "anime":{"count":412,"meanScore":8.4,"minutesWatched":144000,"episodesWatched":6031},
                    "manga":{"count":57,"meanScore":7.9,"chaptersRead":4218,"volumesRead":313}
                }
            }}
            """);

        var viewer = await harness.Client.GetViewerAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(viewer.Id, Is.EqualTo(11));
        Assert.That(viewer.Name, Is.EqualTo("zhollis"));
        Assert.That(viewer.AvatarLarge, Is.EqualTo("https://img/large.png"));
        Assert.That(viewer.Options.TitleLanguage, Is.EqualTo(UserTitleLanguage.English));
        Assert.That(viewer.Options.DisplayAdultContent, Is.True);
        Assert.That(viewer.ScoreFormat, Is.EqualTo(ScoreFormat.Point10Decimal));
        Assert.That(viewer.AnimeSectionOrder, Is.EqualTo(["Watching", "Completed"]));
        // The manga list has its own order and its own names (#12); grouping the manga tab by the
        // anime order would put a "Watching" section it never has ahead of the ones it does.
        Assert.That(viewer.MangaSectionOrder, Is.EqualTo(["Reading", "Completed"]));

        Assert.That(viewer.AnimeStatistics.Count, Is.EqualTo(412));
        Assert.That(viewer.AnimeStatistics.EpisodesWatched, Is.EqualTo(6031));

        // AniList serves one UserStatistics type for both branches, so the manga totals arrive in
        // fields the anime branch never populates — and vice versa. Asserted together so a mapping
        // that read the wrong branch would show up as anime numbers on the manga card (#12).
        Assert.That(viewer.MangaStatistics.Count, Is.EqualTo(57));
        Assert.That(viewer.MangaStatistics.MeanScore, Is.EqualTo(7.9));
        Assert.That(viewer.MangaStatistics.ChaptersRead, Is.EqualTo(4218));
        Assert.That(viewer.MangaStatistics.VolumesRead, Is.EqualTo(313));
    }

    [Test]
    public async Task GetViewer_WithNoMangaStatistics_LeavesTheMangaTotalsAtZero()
    {
        // An account that has never touched manga: AniList omits the branch rather than sending
        // zeros, and the Settings card renders these directly.
        var harness = new Harness().Returns("ViewerFull", """
            {"Viewer":{
                "id":11,"name":"zhollis",
                "statistics":{"anime":{"count":412,"meanScore":8.4,"minutesWatched":144000,"episodesWatched":6031}}
            }}
            """);

        var viewer = await harness.Client.GetViewerAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(viewer.AnimeStatistics.Count, Is.EqualTo(412));
        Assert.That(viewer.MangaStatistics.Count, Is.EqualTo(0));
        Assert.That(viewer.MangaStatistics.ChaptersRead, Is.EqualTo(0));
        Assert.That(viewer.MangaStatistics.VolumesRead, Is.EqualTo(0));
        Assert.That(viewer.MangaStatistics.MeanScore, Is.EqualTo(0));
    }

    [Test]
    public async Task GetViewer_WithNoViewerInTheResponse_Throws()
    {
        // Signed in but no viewer is incoherent; silently returning an empty profile would blank
        // the user's settings screen and then sync those blanks upstream.
        var harness = new Harness().Returns("ViewerFull", """{"Viewer":null}""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Client.GetViewerAsync(TestContext.CurrentContext.CancellationToken));
    }

    [Test]
    public async Task GetViewer_CachesTheIdSoTheListQueryNeedsNoSecondLookup()
    {
        // MediaListCollection needs a userId, and paying a Viewer round-trip for it on every
        // library load is a rate-limit slot spent on something that cannot change.
        var harness = new Harness()
            .Returns("ViewerFull", """{"Viewer":{"id":11,"name":"zhollis"}}""")
            .Returns("MediaListCollection", """{"MediaListCollection":{"lists":[]}}""");

        await harness.Client.GetViewerAsync(TestContext.CurrentContext.CancellationToken);
        await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.CurrentContext.CancellationToken);

        Assert.That(harness.Handler.CallsTo("Viewer"), Is.EqualTo(0));
        Assert.That(harness.Handler.Last.IntVariable("userId"), Is.EqualTo(11));
    }

    // ── Updating settings ────────────────────────────────────────────

    [Test]
    public async Task UpdateUser_SendsOnlyTheFieldsThatWereSet()
    {
        // A PATCH-shaped mutation: sending an unset field would overwrite a preference the user
        // changed on the website.
        var harness = new Harness().Returns("UpdateUser", ViewerPayload);

        await harness.Client.UpdateUserAsync(
            new UpdateUserRequest { DisplayAdultContent = true },
            TestContext.CurrentContext.CancellationToken);

        var request = harness.Handler.Last;
        Assert.That(request.BoolVariable("displayAdultContent"), Is.True);
        Assert.That(request.HasVariable("titleLanguage"), Is.False);
        Assert.That(request.HasVariable("scoreFormat"), Is.False);
        Assert.That(request.HasVariable("notificationOptions"), Is.False);
    }

    [Test]
    public async Task UpdateUser_SendsAniListsSpellingOfEachEnum()
    {
        var harness = new Harness().Returns("UpdateUser", ViewerPayload);

        await harness.Client.UpdateUserAsync(
            new UpdateUserRequest
            {
                TitleLanguage = UserTitleLanguage.Native,
                ScoreFormat = ScoreFormat.Point100,
                StaffNameLanguage = UserStaffNameLanguage.Native,
            },
            TestContext.CurrentContext.CancellationToken);

        var request = harness.Handler.Last;
        Assert.That(request.StringVariable("titleLanguage"), Is.EqualTo("NATIVE"));
        Assert.That(request.StringVariable("scoreFormat"), Is.EqualTo("POINT_100"));
        Assert.That(request.StringVariable("staffNameLanguage"), Is.EqualTo("NATIVE"));
    }

    [Test]
    public async Task UpdateUser_SendsNotificationOptionsAsTypeEnabledPairs()
    {
        var harness = new Harness().Returns("UpdateUser", ViewerPayload);

        await harness.Client.UpdateUserAsync(
            new UpdateUserRequest
            {
                NotificationOptions = [new NotificationOptionInput { Type = "AIRING", Enabled = true }],
            },
            TestContext.CurrentContext.CancellationToken);

        var option = harness.Handler.Last.Variable("notificationOptions").EnumerateArray().Single();
        Assert.That(option.GetProperty("type").GetString(), Is.EqualTo("AIRING"));
        Assert.That(option.GetProperty("enabled").GetBoolean(), Is.True);
    }

    [Test]
    public async Task UpdateUser_ReturnsTheUpdatedProfile()
    {
        var harness = new Harness().Returns("UpdateUser", ViewerPayload);

        var user = await harness.Client.UpdateUserAsync(
            new UpdateUserRequest { DisplayAdultContent = true },
            TestContext.CurrentContext.CancellationToken);

        Assert.That(user.Id, Is.EqualTo(11));
        Assert.That(user.Name, Is.EqualTo("zhollis"));
    }

    // ── The library ──────────────────────────────────────────────────

    [TestCase(MediaKind.Anime, "ANIME")]
    [TestCase(MediaKind.Manga, "MANGA")]
    public async Task GetMediaListGrouped_SendsTheTypeItWasAskedFor(MediaKind kind, string expected)
    {
        // Unlike Search, MediaListCollection REJECTS a missing type outright — verified live, it
        // answers 400 "User ID/Name & Type arguments required" — so there is no absent-vs-null
        // subtlety here, only a type that must always be sent and must be the right one.
        var harness = new Harness()
            .Returns("Viewer", """{"Viewer":{"id":11}}""")
            .Returns("MediaListCollection", """{"MediaListCollection":{"lists":[]}}""");

        await harness.Client.GetMediaListGroupedAsync(kind, TestContext.CurrentContext.CancellationToken);

        Assert.That(harness.Handler.Requests[1].StringVariable("type"), Is.EqualTo(expected));
    }

    [Test]
    public async Task GetMediaListGrouped_ResolvesTheViewerBeforeAskingForTheCollection()
    {
        var harness = new Harness()
            .Returns("Viewer", """{"Viewer":{"id":11}}""")
            .Returns("MediaListCollection", """{"MediaListCollection":{"lists":[]}}""");

        await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.CurrentContext.CancellationToken);

        Assert.That(harness.Handler.Requests[0].OperationName, Is.EqualTo("Viewer"));
        Assert.That(harness.Handler.Requests[1].OperationName, Is.EqualTo("MediaListCollection"));
        Assert.That(harness.Handler.Requests[1].IntVariable("userId"), Is.EqualTo(11));
    }

    [Test]
    public async Task GetMediaListGrouped_KeepsTheServersListNamesAndGrouping()
    {
        // Section order and custom list names are the user's own; regrouping them here would
        // silently reorder their library.
        var harness = new Harness()
            .Returns("Viewer", """{"Viewer":{"id":11}}""")
            .Returns("MediaListCollection", """
                {"MediaListCollection":{"lists":[
                    {"name":"Watching","entries":[
                        {"id":1,"mediaId":10,"status":"CURRENT","progress":3,"updatedAt":86400},
                        {"id":2,"mediaId":11,"status":"CURRENT"}]},
                    {"name":"Completed","entries":[{"id":3,"mediaId":12,"status":"COMPLETED"}]}
                ]}}
                """);

        var groups = await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.CurrentContext.CancellationToken);

        Assert.That(groups.Select(g => g.Name), Is.EqualTo(["Watching", "Completed"]));
        Assert.That(groups[0].Entries.Select(e => e.MediaId), Is.EqualTo([10, 11]));
        Assert.That(groups[0].Entries[0].UpdatedAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(86400)));
    }

    [Test]
    public async Task GetMediaListGrouped_DropsListsThatHaveNoEntries()
    {
        // An empty custom list would otherwise render as a section header with nothing under it.
        var harness = new Harness()
            .Returns("Viewer", """{"Viewer":{"id":11}}""")
            .Returns("MediaListCollection", """
                {"MediaListCollection":{"lists":[
                    {"name":"Paused","entries":[]},
                    {"name":"Watching","entries":[{"id":1,"mediaId":10,"status":"CURRENT"}]}
                ]}}
                """);

        var groups = await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.CurrentContext.CancellationToken);

        Assert.That(groups, Has.Exactly(1).Items);
        Assert.That(groups.Single().Name, Is.EqualTo("Watching"));
    }

    [Test]
    public async Task GetMediaListGrouped_WithAnEmptyLibrary_ReturnsNoGroups()
    {
        var harness = new Harness()
            .Returns("Viewer", """{"Viewer":{"id":11}}""")
            .Returns("MediaListCollection", """{"MediaListCollection":null}""");

        Assert.That(await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.CurrentContext.CancellationToken), Is.Empty);
    }

    // ── Schema migrations (#163) ─────────────────────────────────────
    //
    // These pin the *outgoing query text*, which is normally not worth asserting — it is the
    // fixtures that prove a query works. But both of these are field-level migrations where the
    // old spelling keeps working for now: AniList still answers `averageScore` as an alias of
    // `weightedScore`, and `relationType(version: 2)` still resolves. So a regression here is
    // invisible everywhere else — the app renders, the fixtures replay, and nothing fails until
    // AniList drops the field or someone wonders why no relation ever says "Same Universe".

    [Test]
    public async Task GetMedia_AsksForWeightedScoreRatherThanTheDeprecatedAverageScore()
    {
        var harness = new Harness().Returns("Media", """{"Media":{"id":9,"type":"ANIME"}}""");

        await harness.Client.GetMediaAsync(9, TestContext.CurrentContext.CancellationToken);

        var query = harness.Handler.Last.Query;
        Assert.That(query, Is.Not.Null);
        Assert.That(query, Does.Contain("weightedScore"));
        Assert.That(query, Does.Not.Contain("averageScore"));
    }

    [Test]
    public async Task BrowseAnimePage_AsksForWeightedScore_ViaTheSharedBrowseFragment()
    {
        // BrowseMediaFragment is appended to four operations, so one assertion covers Search,
        // Discover and both browse variants as well.
        var harness = new Harness().Returns("BrowseAnime", EmptyPage);

        await harness.Client.BrowseAnimePageAsync(
            "SCORE_DESC", status: null, season: null, seasonYear: null,
            isAdult: false, format: null, page: 1,
            cancellationToken: TestContext.CurrentContext.CancellationToken);

        var query = harness.Handler.Last.Query;
        Assert.That(query, Is.Not.Null);
        Assert.That(query, Does.Contain("weightedScore"));
        Assert.That(query, Does.Not.Contain("averageScore"));
    }

    [Test]
    public async Task GetMedia_AsksForRelationsVersion3_SoSameUniverseIsNotReportedAsOther()
    {
        var harness = new Harness().Returns("Media", """{"Media":{"id":9,"type":"ANIME"}}""");

        await harness.Client.GetMediaAsync(9, TestContext.CurrentContext.CancellationToken);

        var query = harness.Handler.Last.Query;
        Assert.That(query, Is.Not.Null);
        Assert.That(query, Does.Contain("relationType(version: 3)"));
    }

    [Test]
    public async Task GetMedia_MapsSameUniverseRelation_ToItsDisplayName()
    {
        var harness = new Harness().Returns("Media", """
            {"Media":{"id":9,"type":"ANIME","relations":{"edges":[
                {"relationType":"SAME_UNIVERSE","node":{"id":40,"type":"ANIME"}}
            ]}}}
            """);

        var (media, _) = await harness.Client.GetMediaAsync(9, TestContext.CurrentContext.CancellationToken);

        Assert.That(media, Is.Not.Null);
        Assert.That(media.Relations, Has.Exactly(1).Items);
        var relation = media.Relations.Single();
        Assert.That(relation.RelationType, Is.EqualTo("Same Universe"));
    }

    private const string EmptyPage =
        """{"Page":{"pageInfo":{"currentPage":1,"hasNextPage":false},"media":[]}}""";

    private const string ViewerPayload =
        """{"UpdateUser":{"id":11,"name":"zhollis"}}""";

    private sealed class Harness
    {
        private readonly Dictionary<string, string> _byOperation = new(StringComparer.Ordinal);

        public Harness()
        {
            Handler = new ScriptedGraphQlHandler(request =>
                request.OperationName is { } name && _byOperation.TryGetValue(name, out var data)
                    ? ScriptedGraphQlHandler.Data(data)
                    : ScriptedGraphQlHandler.Raw(
                        HttpStatusCode.OK,
                        $$"""{"errors":[{"message":"No response scripted for {{request.OperationName}}."}]}"""));

            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token-abc");

            Client = new AniListClient(
                new HttpClient(Handler),
                auth,
                Substitute.For<IOutageStateService>(),
                NullLogger<AniListClient>.Instance);
        }

        public ScriptedGraphQlHandler Handler { get; }

        public AniListClient Client { get; }

        public Harness Returns(string operationName, string dataJson)
        {
            _byOperation[operationName] = dataJson;
            return this;
        }
    }
}
