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

    [Fact]
    public async Task BrowseAnimePage_SendsItsPagingAndFilters()
    {
        var harness = new Harness().Returns("BrowseAnime", EmptyPage);

        await harness.Client.BrowseAnimePageAsync(
            "SCORE_DESC", status: "RELEASING", season: "WINTER", seasonYear: 2026,
            isAdult: false, format: "MOVIE", page: 3, perPage: 25,
            cancellationToken: TestContext.Current.CancellationToken);

        var request = harness.Handler.Last;
        Assert.Equal(3, request.IntVariable("page"));
        Assert.Equal(25, request.IntVariable("perPage"));
        Assert.Equal("RELEASING", request.StringVariable("status"));
        Assert.Equal("WINTER", request.StringVariable("season"));
        Assert.Equal(2026, request.IntVariable("seasonYear"));
        Assert.False(request.BoolVariable("isAdult"));
        Assert.Equal("MOVIE", request.StringVariable("format"));
    }

    [Fact]
    public async Task BrowseAnimePage_AppendsAnIdTiebreakerToTheSort()
    {
        // Without a stable secondary key, two media with equal popularity can swap between pages —
        // which shows up as a duplicate card and a missing one after Load More.
        var harness = new Harness().Returns("BrowseAnime", EmptyPage);

        await harness.Client.BrowseAnimePageAsync(
            "POPULARITY_DESC", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["POPULARITY_DESC", "ID"], harness.Handler.Last.StringArrayVariable("sort"));
    }

    [Theory]
    [InlineData("ID")]
    [InlineData("ID_DESC")]
    public async Task BrowseAnimePage_DoesNotTiebreakASortThatIsAlreadyById(string sort)
    {
        var harness = new Harness().Returns("BrowseAnime", EmptyPage);

        await harness.Client.BrowseAnimePageAsync(
            sort, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([sort], harness.Handler.Last.StringArrayVariable("sort"));
    }

    [Fact]
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
            "POPULARITY_DESC", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([5, 6], items.Select(i => i.Node!.Id));

        var first = items[0].Node;
        Assert.NotNull(first);
        Assert.NotNull(first.Title);
        Assert.Equal("Frieren", first.Title.Romaji);

        Assert.NotNull(pageInfo);
        Assert.Equal(2, pageInfo.CurrentPage);
        Assert.True(pageInfo.HasNextPage);
    }

    [Fact]
    public async Task BrowseAnimePage_WithNoPageAtAll_ComesBackEmptyRatherThanThrowing()
    {
        // An empty final page is a normal end-of-list, not an error to surface.
        var harness = new Harness().Returns("BrowseAnime", """{"Page":null}""");

        var (items, pageInfo) = await harness.Client.BrowseAnimePageAsync(
            "POPULARITY_DESC", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(items);
        Assert.Null(pageInfo);
    }

    // ── Media details ────────────────────────────────────────────────

    [Fact]
    public async Task GetMedia_DeserializesTheMediaAndTheViewersOwnEntry()
    {
        var harness = new Harness().Returns("Media", """
            {"Media":{
                "id":9,"type":"ANIME","episodes":12,"averageScore":88,
                "title":{"romaji":"Frieren","english":"Frieren"},
                "mediaListEntry":{"id":555,"status":"CURRENT","progress":4,"score":9.0}
            }}
            """);

        var (media, entry) = await harness.Client.GetMediaAsync(9, TestContext.Current.CancellationToken);

        Assert.NotNull(media);
        Assert.NotNull(entry);
        Assert.Equal(9, media.Id);
        Assert.Equal(12, media.Episodes);
        Assert.Equal(555, entry.Id);
        Assert.Equal(MediaListStatus.Current, entry.Status);
        Assert.Equal(4, entry.Progress);
    }

    [Fact]
    public async Task GetMedia_WhenTheIdIsUnknown_ReturnsNothingRatherThanThrowing()
    {
        // The details page turns this into its own not-found state.
        var harness = new Harness().Returns("Media", """{"Media":null}""");

        var (media, entry) = await harness.Client.GetMediaAsync(9, TestContext.Current.CancellationToken);

        Assert.Null(media);
        Assert.Null(entry);
    }

    [Fact]
    public async Task GetMedia_ForMediaNotOnTheViewersList_HasNoEntry()
    {
        var harness = new Harness().Returns("Media", """{"Media":{"id":9,"type":"ANIME"}}""");

        var (media, entry) = await harness.Client.GetMediaAsync(9, TestContext.Current.CancellationToken);

        Assert.NotNull(media);
        Assert.Null(entry);
    }

    // ── Saving a list entry ──────────────────────────────────────────

    [Fact]
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
            TestContext.Current.CancellationToken);

        var request = harness.Handler.Last;
        Assert.Equal(42, request.IntVariable("mediaId"));
        Assert.Equal("COMPLETED", request.StringVariable("status"));
        Assert.Equal(12, request.IntVariable("progress"));
        // Manga volumes are a second, independent counter (#12); omitting it silently discards
        // whatever the reader had set on AniList.
        Assert.Equal(3, request.IntVariable("progressVolumes"));
        Assert.Equal(8.5, request.Variable("score").GetDouble());
        Assert.Equal(2, request.IntVariable("repeat"));
        Assert.Equal("rewatch", request.StringVariable("notes"));
        Assert.True(request.BoolVariable("private"));
        Assert.True(request.BoolVariable("hiddenFromStatusLists"));
    }

    [Theory]
    [InlineData(MediaListStatus.Current, "CURRENT")]
    [InlineData(MediaListStatus.Planning, "PLANNING")]
    [InlineData(MediaListStatus.Completed, "COMPLETED")]
    [InlineData(MediaListStatus.Dropped, "DROPPED")]
    [InlineData(MediaListStatus.Paused, "PAUSED")]
    [InlineData(MediaListStatus.Repeating, "REPEATING")]
    public async Task SaveMediaListEntry_SendsAniListsSpellingOfEachStatus(
        MediaListStatus status, string expected)
    {
        var harness = new Harness().Returns("SaveMediaListEntry", """{"SaveMediaListEntry":{"id":1}}""");

        await harness.Client.SaveMediaListEntryAsync(
            new MediaListEntry { MediaId = 1, Status = status },
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, harness.Handler.Last.StringVariable("status"));
    }

    [Fact]
    public async Task SaveMediaListEntry_ReturnsTheServersVersionOfTheEntry()
    {
        // Add-to-list adopts the server-assigned id here; losing it orphans every later edit.
        var harness = new Harness().Returns("SaveMediaListEntry", """
            {"SaveMediaListEntry":{"id":777,"mediaId":42,"status":"CURRENT","progress":3,"updatedAt":86400}}
            """);

        var saved = await harness.Client.SaveMediaListEntryAsync(
            new MediaListEntry { MediaId = 42 }, TestContext.Current.CancellationToken);

        Assert.NotNull(saved);
        Assert.Equal(777, saved.Id);
        Assert.Equal(42, saved.MediaId);
        Assert.Equal(3, saved.Progress);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(86400), saved.UpdatedAt);
    }

    [Fact]
    public async Task SaveMediaListEntry_WhenTheServerReturnsNoEntry_ReturnsNull()
    {
        var harness = new Harness().Returns("SaveMediaListEntry", """{"SaveMediaListEntry":null}""");

        var saved = await harness.Client.SaveMediaListEntryAsync(
            new MediaListEntry { MediaId = 42 }, TestContext.Current.CancellationToken);

        Assert.Null(saved);
    }

    // ── Deleting a list entry ────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteMediaListEntry_ReportsWhatTheServerSaid(bool deleted)
    {
        var harness = new Harness().Returns(
            "DeleteMediaListEntry",
            "{\"DeleteMediaListEntry\":{\"deleted\":" + (deleted ? "true" : "false") + "}}");

        var result = await harness.Client.DeleteMediaListEntryAsync(9, TestContext.Current.CancellationToken);

        Assert.Equal(deleted, result);
        Assert.Equal(9, harness.Handler.Last.IntVariable("id"));
    }

    [Fact]
    public async Task DeleteMediaListEntry_WithNoResultAtAll_IsTreatedAsNotDeleted()
    {
        // Reporting success here would remove the row locally while it still exists upstream.
        var harness = new Harness().Returns("DeleteMediaListEntry", """{"DeleteMediaListEntry":null}""");

        Assert.False(await harness.Client.DeleteMediaListEntryAsync(9, TestContext.Current.CancellationToken));
    }

    // ── Favourites ───────────────────────────────────────────────────

    [Theory]
    [InlineData(FavouriteKind.Anime, "animeId")]
    [InlineData(FavouriteKind.Manga, "mangaId")]
    [InlineData(FavouriteKind.Character, "characterId")]
    [InlineData(FavouriteKind.Staff, "staffId")]
    [InlineData(FavouriteKind.Studio, "studioId")]
    public async Task ToggleFavourite_SendsOnlyTheIdBelongingToItsKind(FavouriteKind kind, string expectedVariable)
    {
        // Every other id must be omitted rather than null — sending two would favourite two things.
        var harness = new Harness().Returns("ToggleFavourite", """{"ToggleFavourite":{}}""");

        await harness.Client.ToggleFavouriteAsync(kind, 31, TestContext.Current.CancellationToken);

        var request = harness.Handler.Last;
        Assert.Equal(31, request.IntVariable(expectedVariable));
        foreach (var other in new[] { "animeId", "mangaId", "characterId", "staffId", "studioId" }
                     .Where(v => v != expectedVariable))
        {
            Assert.False(request.HasVariable(other));
        }
    }

    [Fact]
    public async Task ToggleFavourite_SucceedsOnAnEmptyResponseBody()
    {
        // Success is signalled by the absence of errors; the selection set exists only to make the
        // mutation valid.
        var harness = new Harness().Returns("ToggleFavourite", """{"ToggleFavourite":null}""");

        Assert.True(await harness.Client.ToggleFavouriteAsync(
            FavouriteKind.Anime, 31, TestContext.Current.CancellationToken));
    }

    // ── The viewer ───────────────────────────────────────────────────

    [Fact]
    public async Task GetViewer_DeserializesTheProfileAndTheDisplayPreferences()
    {
        var harness = new Harness().Returns("ViewerFull", """
            {"Viewer":{
                "id":11,"name":"zhollis","siteUrl":"https://anilist.co/user/zhollis",
                "avatar":{"large":"https://img/large.png"},
                "options":{"titleLanguage":"ENGLISH","displayAdultContent":true,"staffNameLanguage":"NATIVE"},
                "mediaListOptions":{"scoreFormat":"POINT_10_DECIMAL","animeList":{"sectionOrder":["Watching","Completed"]},"mangaList":{"sectionOrder":["Reading","Completed"]}}
            }}
            """);

        var viewer = await harness.Client.GetViewerAsync(TestContext.Current.CancellationToken);

        Assert.Equal(11, viewer.Id);
        Assert.Equal("zhollis", viewer.Name);
        Assert.Equal("https://img/large.png", viewer.AvatarLarge);
        Assert.Equal(UserTitleLanguage.English, viewer.Options.TitleLanguage);
        Assert.True(viewer.Options.DisplayAdultContent);
        Assert.Equal(ScoreFormat.Point10Decimal, viewer.ScoreFormat);
        Assert.Equal(["Watching", "Completed"], viewer.AnimeSectionOrder);
        // The manga list has its own order and its own names (#12); grouping the manga tab by the
        // anime order would put a "Watching" section it never has ahead of the ones it does.
        Assert.Equal(["Reading", "Completed"], viewer.MangaSectionOrder);
    }

    [Fact]
    public async Task GetViewer_WithNoViewerInTheResponse_Throws()
    {
        // Signed in but no viewer is incoherent; silently returning an empty profile would blank
        // the user's settings screen and then sync those blanks upstream.
        var harness = new Harness().Returns("ViewerFull", """{"Viewer":null}""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Client.GetViewerAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetViewer_CachesTheIdSoTheListQueryNeedsNoSecondLookup()
    {
        // MediaListCollection needs a userId, and paying a Viewer round-trip for it on every
        // library load is a rate-limit slot spent on something that cannot change.
        var harness = new Harness()
            .Returns("ViewerFull", """{"Viewer":{"id":11,"name":"zhollis"}}""")
            .Returns("MediaListCollection", """{"MediaListCollection":{"lists":[]}}""");

        await harness.Client.GetViewerAsync(TestContext.Current.CancellationToken);
        await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.Handler.CallsTo("Viewer"));
        Assert.Equal(11, harness.Handler.Last.IntVariable("userId"));
    }

    // ── Updating settings ────────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_SendsOnlyTheFieldsThatWereSet()
    {
        // A PATCH-shaped mutation: sending an unset field would overwrite a preference the user
        // changed on the website.
        var harness = new Harness().Returns("UpdateUser", ViewerPayload);

        await harness.Client.UpdateUserAsync(
            new UpdateUserRequest { DisplayAdultContent = true },
            TestContext.Current.CancellationToken);

        var request = harness.Handler.Last;
        Assert.True(request.BoolVariable("displayAdultContent"));
        Assert.False(request.HasVariable("titleLanguage"));
        Assert.False(request.HasVariable("scoreFormat"));
        Assert.False(request.HasVariable("notificationOptions"));
    }

    [Fact]
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
            TestContext.Current.CancellationToken);

        var request = harness.Handler.Last;
        Assert.Equal("NATIVE", request.StringVariable("titleLanguage"));
        Assert.Equal("POINT_100", request.StringVariable("scoreFormat"));
        Assert.Equal("NATIVE", request.StringVariable("staffNameLanguage"));
    }

    [Fact]
    public async Task UpdateUser_SendsNotificationOptionsAsTypeEnabledPairs()
    {
        var harness = new Harness().Returns("UpdateUser", ViewerPayload);

        await harness.Client.UpdateUserAsync(
            new UpdateUserRequest
            {
                NotificationOptions = [new NotificationOptionInput { Type = "AIRING", Enabled = true }],
            },
            TestContext.Current.CancellationToken);

        var option = harness.Handler.Last.Variable("notificationOptions").EnumerateArray().Single();
        Assert.Equal("AIRING", option.GetProperty("type").GetString());
        Assert.True(option.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task UpdateUser_ReturnsTheUpdatedProfile()
    {
        var harness = new Harness().Returns("UpdateUser", ViewerPayload);

        var user = await harness.Client.UpdateUserAsync(
            new UpdateUserRequest { DisplayAdultContent = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(11, user.Id);
        Assert.Equal("zhollis", user.Name);
    }

    // ── The library ──────────────────────────────────────────────────

    [Theory]
    [InlineData(MediaKind.Anime, "ANIME")]
    [InlineData(MediaKind.Manga, "MANGA")]
    public async Task GetMediaListGrouped_SendsTheTypeItWasAskedFor(MediaKind kind, string expected)
    {
        // Unlike Search, MediaListCollection REJECTS a missing type outright — verified live, it
        // answers 400 "User ID/Name & Type arguments required" — so there is no absent-vs-null
        // subtlety here, only a type that must always be sent and must be the right one.
        var harness = new Harness()
            .Returns("Viewer", """{"Viewer":{"id":11}}""")
            .Returns("MediaListCollection", """{"MediaListCollection":{"lists":[]}}""");

        await harness.Client.GetMediaListGroupedAsync(kind, TestContext.Current.CancellationToken);

        Assert.Equal(expected, harness.Handler.Requests[1].StringVariable("type"));
    }

    [Fact]
    public async Task GetMediaListGrouped_ResolvesTheViewerBeforeAskingForTheCollection()
    {
        var harness = new Harness()
            .Returns("Viewer", """{"Viewer":{"id":11}}""")
            .Returns("MediaListCollection", """{"MediaListCollection":{"lists":[]}}""");

        await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.Current.CancellationToken);

        Assert.Equal("Viewer", harness.Handler.Requests[0].OperationName);
        Assert.Equal("MediaListCollection", harness.Handler.Requests[1].OperationName);
        Assert.Equal(11, harness.Handler.Requests[1].IntVariable("userId"));
    }

    [Fact]
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

        var groups = await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.Current.CancellationToken);

        Assert.Equal(["Watching", "Completed"], groups.Select(g => g.Name));
        Assert.Equal([10, 11], groups[0].Entries.Select(e => e.MediaId));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(86400), groups[0].Entries[0].UpdatedAt);
    }

    [Fact]
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

        var groups = await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.Current.CancellationToken);

        Assert.Equal("Watching", Assert.Single(groups).Name);
    }

    [Fact]
    public async Task GetMediaListGrouped_WithAnEmptyLibrary_ReturnsNoGroups()
    {
        var harness = new Harness()
            .Returns("Viewer", """{"Viewer":{"id":11}}""")
            .Returns("MediaListCollection", """{"MediaListCollection":null}""");

        Assert.Empty(await harness.Client.GetMediaListGroupedAsync(MediaKind.Anime, TestContext.Current.CancellationToken));
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
