using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #225. The Library skips reloading for five minutes after a load, and used to force a reload only
/// for changes it made itself. A status changed on the details page (or Discover, Search, Up Next)
/// left the title in its old section, and an edit from that stale card saved the old status back.
/// Saves go through the real <see cref="CachingAniListClient"/> here, since it is what tells the
/// Library, through <see cref="ListChangeTracker"/>.
/// </summary>
[NonParallelizable]
public class MediaListOutsideChangeReloadTests
{
    private static readonly TimeSpan PastTheDebounce = TimeSpan.FromMilliseconds(1600);

    public MediaListOutsideChangeReloadTests() => TestDataBuilder.ResetAppSettings();

    [Test]
    public async Task ReturningToLibrary_AfterAStatusChangeMadeElsewhere_Reloads()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();

        // The details page moves the title to Paused, then the user goes back, well inside the window.
        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task ReturningToLibrary_AfterARemovalMadeElsewhere_Reloads()
    {
        var harness = new Harness();
        harness.Inner.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        await harness.Model.LoadAsync();

        await harness.Client.DeleteMediaListEntryAsync(101);
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task ReturningToLibrary_ReloadsOnlyOnce_PerChange()
    {
        // Rate-limit budget: the change costs one reload, not one per appearance afterwards.
        var harness = new Harness();
        await harness.Model.LoadAsync();

        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        await harness.Model.LoadAsync();
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task ReturningToLibrary_AfterAFailedSaveElsewhere_StillShortCircuits()
    {
        var harness = new Harness();
        harness.Inner.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns<MediaListEntry?>(_ => throw new HttpRequestException("offline"));
        await harness.Model.LoadAsync();

        Assert.That(
            () => harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused)),
            Throws.TypeOf<HttpRequestException>());
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(1);
    }

    [Test]
    public async Task ReturningToLibrary_AfterARemovalThatDidNotHappen_StillShortCircuits()
    {
        var harness = new Harness();
        harness.Inner.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(false);
        await harness.Model.LoadAsync();

        await harness.Client.DeleteMediaListEntryAsync(101);
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(1);
    }

    [Test]
    public async Task ReturningToLibrary_AfterItsOwnPlusOne_StillShortCircuits()
    {
        // The +1 card already shows the new progress; spending a reload on it would cost two
        // requests on every return after tapping +1.
        var harness = new Harness();
        await harness.Model.LoadAsync();

        var increment = harness.Model.IncrementProgressCommand.ExecuteAsync(harness.FirstEntry());
        await harness.AdvancePastDebounceAsync();
        await increment;
        await harness.Inner.Received(1).SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());

        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(1);
    }

    [Test]
    public async Task ReturningToLibrary_AfterItsOwnPlusOne_StillReloadsForAChangeMadeElsewhere()
    {
        // Absorbing its own save must not swallow one made elsewhere that it has not shown yet.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(2, status: MediaListStatus.Paused));

        var increment = harness.Model.IncrementProgressCommand.ExecuteAsync(harness.FirstEntry());
        await harness.AdvancePastDebounceAsync();
        await increment;
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task ReturningToLibrary_AfterItsOwnProgressEdit_StillShortCircuits()
    {
        // The long-press menu's in-place saves (progress, rating) come back through the coordinator's
        // OnEntrySavedInPlaceAsync, not the +1 path; their card already shows the change too.
        var harness = new Harness();
        await harness.Model.LoadAsync();

        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.EditProgress;
        harness.Dialogs.EditProgressAnswer = 7;
        await harness.Model.ShowActionMenuCommand.ExecuteAsync(harness.FirstEntry());
        await harness.Inner.Received(1).SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());

        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(1);
    }

    [Test]
    public async Task ASaveAnsweredWithNoEntry_StillCountsAsAChange()
    {
        // Whether AniList returned the entry says nothing about whether the write landed; a needless
        // reload is the cheap way to be wrong.
        var harness = new Harness();
        harness.Inner.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MediaListEntry?>(null));
        await harness.Model.LoadAsync();

        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task ASaveLandingWhileTheListIsInFlight_StillReloadsNextTime()
    {
        // The fetched list may predate the save, so the version the load records is the one read
        // before it went out.
        var harness = new Harness();
        await harness.Model.LoadAsync();

        var gate = new TaskCompletionSource<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Inner.GetMediaListGroupedAsync(Arg.Any<MediaKind>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var refresh = harness.Model.LoadAsync(forceReload: true);

        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        gate.SetResult(Harness.Groups());
        await refresh;
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(3);
    }

    [Test]
    public async Task TheAnimeAndMangaLists_EachReloadForTheSameChange()
    {
        // A counter rather than a flag: the first list to reload must not hide the change from the other.
        var harness = new Harness();
        var manga = harness.CreateModel(MediaKind.Manga);
        await harness.Model.LoadAsync();
        await manga.LoadAsync();

        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        await harness.Model.LoadAsync();
        await manga.LoadAsync();

        await harness.Inner.Received(2).GetMediaListGroupedAsync(MediaKind.Anime, Arg.Any<CancellationToken>());
        await harness.Inner.Received(2).GetMediaListGroupedAsync(MediaKind.Manga, Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);
        private readonly IPreferences _preferences = Substitute.For<IPreferences>();
        private readonly IAuthService _auth = Substitute.For<IAuthService>();

        public Harness()
        {
            _preferences.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(c => c.ArgAt<string>(1));
            _preferences.Get(Arg.Any<string>(), Arg.Any<bool>()).Returns(c => c.ArgAt<bool>(1));
            _preferences.Get(Arg.Any<string>(), Arg.Any<int>()).Returns(c => c.ArgAt<int>(1));
            _auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

            Inner.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(new AniListUser { Id = 1, Name = "zhollis" });
            Inner.GetMediaListGroupedAsync(Arg.Any<MediaKind>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Groups()));
            Inner.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<MediaListEntry?>(new MediaListEntry { Id = call.Arg<MediaListEntry>().Id }));

            Client = new CachingAniListClient(Inner, Tracker);
            Model = CreateModel(MediaKind.Anime, Dialogs);
        }

        /// <summary>A fresh copy each read, as AniList gives one.</summary>
        public static IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> Groups() =>
        [
            ("Watching", new List<MediaListEntry>
            {
                TestDataBuilder.Entry(1, progress: 3, status: MediaListStatus.Current, episodes: 12),
                TestDataBuilder.Entry(2, progress: 1, status: MediaListStatus.Current, episodes: 12),
            })
        ];

        /// <summary>The Anime model's dialogs, scripted per test.</summary>
        public ScriptedDialogService Dialogs { get; } = new();

        public IAniListClient Inner { get; } = Substitute.For<IAniListClient>();

        public ListChangeTracker Tracker { get; } = new();

        /// <summary>What every page resolves as <see cref="IAniListClient"/>.</summary>
        public CachingAniListClient Client { get; }

        public MediaListPageModel Model { get; }

        public MediaListPageModel CreateModel(MediaKind kind, ScriptedDialogService? dialogs = null)
        {
            dialogs ??= new ScriptedDialogService();
            var flow = new ListEntryStatusFlow(dialogs);
            var feedback = new RecordingUserFeedback();
            var errors = new ErrorReportService(NullLogger<ErrorReportService>.Instance);
            var navigation = Substitute.For<INavigationService>();
            var upNext = Substitute.For<IUpNextOffer>();
            return kind == MediaKind.Manga
                ? new MangaLibraryPageModel(Client, _auth, Substitute.For<IAiringNotificationService>(), errors, _preferences, navigation, dialogs, feedback,
                    flow, upNext, Tracker, _time, NullLogger<MangaLibraryPageModel>.Instance)
                : new AnimeLibraryPageModel(Client, _auth, Substitute.For<IAiringNotificationService>(), errors,
                    _preferences, navigation, dialogs, feedback, flow, upNext, Tracker, _time,
                    NullLogger<AnimeLibraryPageModel>.Instance);
        }

        public MediaListEntry FirstEntry() => Model.Sections.SelectMany(s => s.AllItems).First(e => e.MediaId == 1);

        public Task AssertListFetchesAsync(int expected)
            => Inner.Received(expected).GetMediaListGroupedAsync(Arg.Any<MediaKind>(), Arg.Any<CancellationToken>());

        /// <summary>Lets the fire-and-forget debounce register its timer, then moves the clock past it.</summary>
        public async Task AdvancePastDebounceAsync()
        {
            for (var i = 0; i < 8; i++)
            {
                await Task.Yield();
            }

            _time.Advance(PastTheDebounce);

            for (var i = 0; i < 8; i++)
            {
                await Task.Yield();
            }
        }
    }
}
