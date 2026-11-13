using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// What actually differs between the two halves of the Library tab (#12). One
/// <see cref="MediaListPageModel"/> serves both, so these are the seams where a wrong answer would
/// be invisible — the anime half kept working either way and only manga would be broken.
/// </summary>
[NonParallelizable]
public class MediaListPageModelKindTests
{
    public MediaListPageModelKindTests() => TestDataBuilder.ResetAppSettings();

    [TestCase(false, MediaKind.Anime)]
    [TestCase(true, MediaKind.Manga)]
    public async Task EachHalf_AsksForItsOwnMediaType(bool manga, MediaKind expected)
    {
        var harness = new Harness(manga);
        harness.ReturnsGroups(("Watching", [TestDataBuilder.Entry(1)]));

        await harness.Model.LoadAsync();

        await harness.Client.Received(1)
            .GetMediaListGroupedAsync(expected, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EachHalf_OrdersSectionsByItsOwnSectionOrder()
    {
        // The names differ, so ordering the manga tab by the anime order would sort every manga
        // section against names it never contains and leave them in server order.
        AppSettings.AnimeSectionOrder = ["Completed", "Watching"];
        AppSettings.MangaSectionOrder = ["Completed", "Reading"];

        var manga = new Harness(manga: true);
        manga.ReturnsGroups(
            ("Reading", [TestDataBuilder.Entry(1, mediaType: "MANGA")]),
            ("Completed", [TestDataBuilder.Entry(2, mediaType: "MANGA")]));

        await manga.Model.LoadAsync();

        Assert.That(manga.Model.Sections.Select(s => s.Title), Is.EqualTo(["Completed", "Reading"]));
    }

    [Test]
    public async Task TheSortPreference_IsNotSharedBetweenTheHalves()
    {
        // Sorting your manga by title must not silently reorder your anime.
        var preferences = new FakePreferences();
        var anime = new Harness(manga: false, preferences);
        var manga = new Harness(manga: true, preferences);
        anime.ReturnsGroups(("Watching", [TestDataBuilder.Entry(1)]));
        manga.ReturnsGroups(("Reading", [TestDataBuilder.Entry(2, mediaType: "MANGA")]));
        await anime.Model.LoadAsync();
        await manga.Model.LoadAsync();

        manga.Model.SelectSortCommand.Execute("Title:asc");

        Assert.That(manga.Model.CurrentSortField, Is.EqualTo(SortField.Title));
        Assert.That(anime.Model.CurrentSortField, Is.EqualTo(SortField.LastUpdated));
        Assert.That(preferences.Get("manga_sort_field", string.Empty), Is.EqualTo("Title"));
        Assert.That(preferences.Get("anime_sort_field", string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void TheViewMode_IsSharedDeliberately()
    {
        // Unlike sort, view mode is one app-wide setting shared with the media-browse View All
        // lists, so the Library halves read the same key rather than carving out an exception.
        var preferences = new FakePreferences();
        var anime = new Harness(manga: false, preferences);

        anime.Model.CycleViewModeCommand.Execute(null);
        var expected = anime.Model.CurrentViewMode;

        Assert.That(new Harness(manga: true, preferences).Model.CurrentViewMode, Is.EqualTo(expected));
    }

    // ── The empty state ──────────────────────────────────────────────

    [Test]
    public async Task ASuccessfulLoadWithNothingOnTheList_IsEmptyRatherThanContent()
    {
        // Rendering zero sections is a blank page, and this is the common case for manga — plenty
        // of AniList accounts track anime only.
        var harness = new Harness(manga: true);
        harness.ReturnsGroups();

        await harness.Model.LoadAsync();

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Empty));
        Assert.That(harness.Model.CurrentStateKey, Is.EqualTo(nameof(PageState.Empty)));
    }

    [Test]
    public async Task AnEmptyListIsNotAnError()
    {
        // The two are one branch apart — both end with no sections on screen — and conflating them
        // would offer a Try Again button for a list that loaded perfectly well.
        var harness = new Harness(manga: true);
        harness.ReturnsGroups();

        await harness.Model.LoadAsync();

        Assert.That(harness.Model.CurrentState, Is.Not.EqualTo(PageState.Error));
        Assert.That(harness.Model.ErrorTitle, Is.Empty);
    }

    [Test]
    public async Task AListEmptiedByTheAdultFilter_AlsoLandsOnEmpty()
    {
        // The state is decided from the sections actually built, not the groups the server sent,
        // so a list whose only entries are filtered out reads as empty rather than as content.
        AppSettings.DisplayAdultContent = false;
        var harness = new Harness(manga: true);
        harness.ReturnsGroups(("Reading", [TestDataBuilder.Entry(1, mediaType: "MANGA", isAdult: true)]));

        await harness.Model.LoadAsync();

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Empty));
    }

    [Test]
    public async Task AListWithEntries_ReachesContent()
    {
        var harness = new Harness(manga: true);
        harness.ReturnsGroups(("Reading", [TestDataBuilder.Entry(1, mediaType: "MANGA")]));

        await harness.Model.LoadAsync();

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Content));
        Assert.That(harness.Model.CurrentStateKey, Is.Null);
    }

    // ── Section defaults ─────────────────────────────────────────────

    [Test]
    public async Task RereadingDefaultsToExpanded_AsRewatchingDoesForAnime()
    {
        // The re-consuming section is the one a returning reader wants open; it is named
        // differently per type, so matching only "Rewatching" would collapse it for manga.
        var harness = new Harness(manga: true);
        harness.ReturnsGroups(
            ("Reading", [TestDataBuilder.Entry(1, mediaType: "MANGA")]),
            ("Completed", [TestDataBuilder.Entry(2, mediaType: "MANGA")]),
            ("Rereading", [TestDataBuilder.Entry(3, mediaType: "MANGA")]));

        await harness.Model.LoadAsync();

        var sections = harness.Model.Sections.ToDictionary(s => s.Title);
        Assert.That(sections["Reading"].IsExpanded, Is.True);      // first section
        Assert.That(sections["Rereading"].IsExpanded, Is.True);
        Assert.That(sections["Completed"].IsExpanded, Is.False);
    }

    // ── Airing notifications are anime-only ──────────────────────────

    [Test]
    public async Task TheMangaHalf_NeverTouchesTheAiringNotificationService()
    {
        // Manga does not air. Caching ids for the background worker or asking for notification
        // permission from this half would both be meaningless, and the permission prompt would be
        // user-visible nonsense.
        var harness = new Harness(manga: true);
        harness.ReturnsGroups(("Reading", [TestDataBuilder.Entry(1, mediaType: "MANGA")]));

        await harness.Model.LoadAsync();

        await harness.Airing.DidNotReceiveWithAnyArgs().RequestPermissionAsync();
        harness.Airing.DidNotReceiveWithAnyArgs().SchedulePeriodicCheck();
    }

    [Test]
    public async Task TheAnimeHalf_StillCachesForTheWorker()
    {
        // The counterpart assertion: the hook exists to be overridden, not to disable the feature.
        var preferences = new FakePreferences();
        var harness = new Harness(manga: false, preferences);
        harness.ReturnsGroups(("Watching", [TestDataBuilder.Entry(1, mediaStatus: "RELEASING")]));

        await harness.Model.LoadAsync();

        Assert.That(AiringNotificationState.ReadMediaIds(preferences), Is.EqualTo([1]));
    }

    private sealed class Harness
    {
        public Harness(bool manga, FakePreferences? preferences = null)
        {
            Preferences = preferences ?? new FakePreferences();
            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

            var dialogs = new ScriptedDialogService();
            Model = manga
                ? new MangaLibraryPageModel(
                    Client, auth, Airing, new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                    Preferences, Substitute.For<INavigationService>(), dialogs, new RecordingUserFeedback(),
                    new ListEntryStatusFlow(dialogs), TimeProvider.System,
                    NullLogger<MediaListPageModel>.Instance)
                : new AnimeLibraryPageModel(
                    Client, auth, Airing, new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                    Preferences, Substitute.For<INavigationService>(), dialogs, new RecordingUserFeedback(),
                    new ListEntryStatusFlow(dialogs), TimeProvider.System,
                    NullLogger<MediaListPageModel>.Instance);
        }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public IAiringNotificationService Airing { get; } = Substitute.For<IAiringNotificationService>();

        public FakePreferences Preferences { get; }

        public MediaListPageModel Model { get; }

        public void ReturnsGroups(params (string Name, IReadOnlyList<MediaListEntry> Entries)[] groups)
        {
            Client.GetMediaListGroupedAsync(Arg.Any<MediaKind>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<(string, IReadOnlyList<MediaListEntry>)>>(
                    groups.Select(g => (g.Name, g.Entries)).ToList()));
            Client.GetViewerAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<AniListUser>(new InvalidOperationException("viewer sync not under test")));
        }
    }
}
