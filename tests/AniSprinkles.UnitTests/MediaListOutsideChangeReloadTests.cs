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
    public async Task ReturningToLibrary_AfterASaveElsewhereThatThrew_StillReloads()
    {
        // A timeout or a lost response can follow a write that landed (Copilot on #233), so a save
        // that throws is not proof nothing changed.
        var harness = new Harness();
        harness.Inner.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns<MediaListEntry?>(_ => throw new HttpRequestException("offline"));
        await harness.Model.LoadAsync();

        Assert.That(
            () => harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused)),
            Throws.TypeOf<HttpRequestException>());
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(2);
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
    public async Task ReturningToLibrary_AfterItsOwnPlusOne_StillReloadsForAChangeMadeDuringIt()
    {
        // Absorbing its own save must not swallow a change that landed while that save was out:
        // the version moved by two, and only one of them is on screen.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var ownSave = new TaskCompletionSource<MediaListEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Inner.SaveMediaListEntryAsync(Arg.Is<MediaListEntry>(e => e.MediaId == 1), Arg.Any<CancellationToken>())
            .Returns(ownSave.Task);

        var increment = harness.Model.IncrementProgressCommand.ExecuteAsync(harness.FirstEntry());
        await harness.AdvancePastDebounceAsync();
        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(2, status: MediaListStatus.Paused));
        ownSave.SetResult(new MediaListEntry { Id = 101 });
        await increment;
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task ReturningToLibrary_AfterItsOwnPlusOne_StillReloadsWhenALoadFinishedDuringTheSave()
    {
        // Copilot on #233: a load that finishes while the +1 is out has merged what AniList held
        // before it, so the card may no longer show the +1. The save's bump is not this card's to
        // absorb then.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var save = harness.HoldNextSave();

        var increment = harness.Model.IncrementProgressCommand.ExecuteAsync(harness.FirstEntry());
        await harness.AdvancePastDebounceAsync();
        await harness.Model.LoadAsync(forceReload: true).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(harness.FirstEntry().Progress, Is.EqualTo(3), "the load put the card back to what AniList held");

        save.SetResult(new MediaListEntry { Id = 101 });
        await increment;
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(3);
    }

    // ── Saves still in flight when the Library appears ───────────────

    [Test]
    public async Task AppearingWhileASaveIsInFlight_WaitsForIt_ThenReloads()
    {
        // Back pressed as the details page sent its edit: the save lands just after the Library has
        // appeared. Checking at once would find nothing changed and keep the old values on screen.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var save = harness.HoldNextSave();

        var pending = harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        var appearing = harness.Model.LoadAsync();
        await harness.YieldAsync();
        Assert.That(appearing.IsCompleted, Is.False, "the appearance went ahead with the save still out");

        save.SetResult(new MediaListEntry { Id = 101 });
        await pending;
        await appearing.WaitAsync(TimeSpan.FromSeconds(5));

        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task AppearingWhileASaveIsInFlight_StillEndsTheWait_WhenTheSaveFails()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var save = harness.HoldNextSave();

        var pending = harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        var appearing = harness.Model.LoadAsync();
        save.SetException(new HttpRequestException("offline"));
        Assert.That(async () => await pending, Throws.TypeOf<HttpRequestException>());
        await appearing.WaitAsync(TimeSpan.FromSeconds(5));

        // Counted as a change too: a failure does not prove the write missed.
        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task AppearingWhileASaveStalls_GivesUpAfterFiveSeconds_AndReloadsNextTime()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var save = harness.HoldNextSave();

        var pending = harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        var appearing = harness.Model.LoadAsync();
        await harness.YieldAsync();
        harness.Time.Advance(TimeSpan.FromMilliseconds(4900));
        await harness.YieldAsync();
        Assert.That(appearing.IsCompleted, Is.False, "gave up before the cap");

        harness.Time.Advance(TimeSpan.FromMilliseconds(200));
        await appearing;
        await harness.AssertListFetchesAsync(1);

        save.SetResult(new MediaListEntry { Id = 101 });
        await pending;
        await harness.Model.LoadAsync();
        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task PullToRefresh_DoesNotWaitForASaveInFlight()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var save = harness.HoldNextSave();
        var pending = harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));

        // Bounded, so a regression fails here rather than hanging the run on the held save.
        await harness.Model.LoadAsync(forceReload: true).WaitAsync(TimeSpan.FromSeconds(5));

        await harness.AssertListFetchesAsync(2);
        save.SetResult(new MediaListEntry { Id = 101 });
        await pending;
    }

    // ── A details edit still waiting on its debounce ─────────────────

    [Test]
    public async Task AppearingBeforeTheDetailsPageIsTold_WaitsForItsUnsentEdit_ThenReloads()
    {
        // The order a device runs it in: going Back, the Library appears and checks before the
        // details page's OnNavigatedFrom sends the edit. Each half was tested alone and passed.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var details = await harness.OpenDetailsAsync();
        await details.IncrementProgressCommand.ExecuteAsync(null);

        var appearing = harness.Model.LoadAsync();
        await harness.YieldAsync();
        Assert.That(appearing.IsCompleted, Is.False, "the appearance went ahead with the edit unsent");

        await details.FlushPendingSaveAsync();
        await appearing.WaitAsync(TimeSpan.FromSeconds(5));

        await harness.AssertListFetchesAsync(2);
        await harness.AssertSavesAsync(1);
    }

    [Test]
    public async Task LeavingWhileAppearing_StillWaits_ForTheSaveTheFlushSent()
    {
        // The flush cancels the debounce, which lets go of the edit; the wait carries over to the
        // save it sends.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var details = await harness.OpenDetailsAsync();
        await details.IncrementProgressCommand.ExecuteAsync(null);
        var save = harness.HoldNextSave();

        var appearing = harness.Model.LoadAsync();
        var flush = details.FlushPendingSaveAsync();
        await harness.YieldAsync();
        Assert.That(appearing.IsCompleted, Is.False, "the appearance went ahead with the save still out");

        save.SetResult(new MediaListEntry { Id = 101 });
        await flush;
        await appearing.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.AssertListFetchesAsync(2);
    }

    [Test]
    public async Task AppearingWithAnUnsentEdit_ThatNobodyFlushes_WaitsForItsOwnTimer()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var details = await harness.OpenDetailsAsync();
        await details.IncrementProgressCommand.ExecuteAsync(null);

        await harness.Model.LoadAsync().WaitAsync(TimeSpan.FromSeconds(10));

        await harness.AssertListFetchesAsync(2);
        await harness.AssertSavesAsync(1);
    }

    [Test]
    public async Task TwoQuickDetailsEdits_AreStillWaitedFor_AndSavedOnce()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var details = await harness.OpenDetailsAsync();
        await details.IncrementProgressCommand.ExecuteAsync(null);
        await details.IncrementProgressCommand.ExecuteAsync(null);

        var appearing = harness.Model.LoadAsync();
        await harness.YieldAsync();
        Assert.That(appearing.IsCompleted, Is.False, "the appearance went ahead with the edit unsent");
        await details.FlushPendingSaveAsync();
        await appearing.WaitAsync(TimeSpan.FromSeconds(5));

        await harness.AssertSavesAsync(1);
        await harness.AssertNothingHeldAsync();
    }

    [Test]
    public async Task AStatusPickThatCarriesTheEdit_LetsItGo()
    {
        // Nothing left held, or every Library appearance afterwards would wait out the 5 s cap.
        var harness = new Harness();
        var details = await harness.OpenDetailsAsync();
        await details.IncrementProgressCommand.ExecuteAsync(null);

        await details.ApplyStatusPickCommand.ExecuteAsync(nameof(MediaListStatus.Paused));

        await harness.AssertNothingHeldAsync();
        await harness.AssertSavesAsync(1);
    }

    [Test]
    public async Task AnEditWhoseSaveFails_IsLetGo()
    {
        var harness = new Harness();
        var details = await harness.OpenDetailsAsync();
        harness.Inner.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns<MediaListEntry?>(_ => throw new HttpRequestException("offline"));
        await details.IncrementProgressCommand.ExecuteAsync(null);

        await details.FlushPendingSaveAsync();

        await harness.AssertNothingHeldAsync();
    }

    [Test]
    public async Task PlusOne_WhileADetailsEditIsUnsent_CountsFromThatEdit()
    {
        // Back, then +1 on the same card before the details edit has landed: the card still reads 3,
        // and saving 4 from it would lose one of the two.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var details = await harness.OpenDetailsAsync();
        await details.IncrementProgressCommand.ExecuteAsync(null);

        var increment = harness.Model.IncrementProgressCommand.ExecuteAsync(harness.FirstEntry());
        harness.TitleOneProgress = 4;
        await details.FlushPendingSaveAsync();
        Assert.That(() => harness.FirstEntry().Progress, Is.EqualTo(5).After(5000, 20));
        await harness.AdvancePastDebounceAsync();
        await increment.WaitAsync(TimeSpan.FromSeconds(5));

        await harness.Inner.Received(1).SaveMediaListEntryAsync(
            Arg.Is<MediaListEntry>(e => e.MediaId == 1 && e.Progress == 5), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PlusOne_WhileAnotherCardsPlusOneIsSaving_DoesNotWaitForIt()
    {
        // The Library's own save is no reason to hold a tap: its card already shows the change.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var save = harness.HoldNextSave();
        var first = harness.Model.IncrementProgressCommand.ExecuteAsync(harness.FirstEntry());
        await harness.AdvancePastDebounceAsync();
        var other = harness.Model.Sections.SelectMany(s => s.AllItems).First(e => e.MediaId == 2);

        var second = harness.Model.IncrementProgressCommand.ExecuteAsync(other);
        await harness.YieldAsync();

        Assert.That(other.Progress, Is.EqualTo(2), "the tap waited for the other card's save");
        save.SetResult(new MediaListEntry { Id = 101 });
        await first;
        await harness.AdvancePastDebounceAsync();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ── Edits from a card that has not caught up yet ─────────────────

    [Test]
    public async Task PlusOne_OnACardChangedElsewhere_RefreshesFirst_AndCountsFromTheNewProgress()
    {
        // Progress went 3 → 7 on the details page. A +1 from the old card would save 4 over it.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        harness.TitleOneProgress = 7;
        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, progress: 7));
        harness.Inner.ClearReceivedCalls();

        var card = harness.FirstEntry();
        var increment = harness.Model.IncrementProgressCommand.ExecuteAsync(card);
        await harness.AdvancePastDebounceAsync();
        await increment;

        await harness.Inner.Received(1).GetMediaListGroupedAsync(Arg.Any<MediaKind>(), Arg.Any<CancellationToken>());
        await harness.Inner.Received(1).SaveMediaListEntryAsync(
            Arg.Is<MediaListEntry>(e => e.MediaId == 1 && e.Progress == 8), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheLongPressMenu_OnACardChangedElsewhere_RefreshesBeforeItOpens()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));

        await harness.Model.ShowActionMenuCommand.ExecuteAsync(harness.FirstEntry());

        await harness.AssertListFetchesAsync(2);
        Assert.That(harness.Dialogs.Calls, Is.Not.Empty, "the menu opened once the list was current");
    }

    [Test]
    public async Task AnEdit_WhenTheRefreshFails_IsRefused_AndSaysWhy()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        await harness.Client.SaveMediaListEntryAsync(TestDataBuilder.Entry(1, status: MediaListStatus.Paused));
        harness.Inner.GetMediaListGroupedAsync(Arg.Any<MediaKind>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>>(_ => throw new HttpRequestException("offline"));
        harness.Inner.ClearReceivedCalls();

        await harness.Model.ShowActionMenuCommand.ExecuteAsync(harness.FirstEntry());
        // Started, then the clock moved, then awaited: a +1 that wrongly went ahead waits on its debounce.
        var increment = harness.Model.IncrementProgressCommand.ExecuteAsync(harness.FirstEntry());
        await harness.AdvancePastDebounceAsync();
        await increment;

        Assert.That(harness.Dialogs.Calls, Is.Empty, "the menu opened on a stale card");
        await harness.Inner.DidNotReceive().SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
        Assert.That(harness.Feedback.Snackbars, Has.Some.Contains("Couldn't refresh your list"));
    }

    [Test]
    public async Task AnEdit_OnACardRemovedElsewhere_DoesNothing()
    {
        var harness = new Harness();
        harness.Inner.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        await harness.Model.LoadAsync();
        var card = harness.FirstEntry();
        harness.TitleOneListed = false;
        await harness.Client.DeleteMediaListEntryAsync(card.Id);

        await harness.Model.ShowActionMenuCommand.ExecuteAsync(card);

        Assert.That(harness.Dialogs.Calls, Is.Empty);
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
    public async Task APopupClosingDuringItsOwnProgressEdit_CostsNoReload()
    {
        // The long-press popups re-fire the Library's appearance as they close, mid-flow, with the
        // flow's own save still out.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var save = harness.HoldNextSave();
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.EditProgress;
        harness.Dialogs.EditProgressAnswer = 7;

        var flow = harness.Model.ShowActionMenuCommand.ExecuteAsync(harness.FirstEntry());
        await harness.YieldAsync();
        await harness.Model.LoadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        save.SetResult(new MediaListEntry { Id = 101 });
        await flow;
        await harness.Model.LoadAsync();

        await harness.AssertListFetchesAsync(1);
    }
    [Test]
    public async Task APopupClosingAfterItsOwnSaveLanded_CostsNoReload()
    {
        // The other half: the save has landed and the flow is still showing its toast when the
        // closing popup re-fires the appearance, before the flow takes up its own bump.
        var harness = new Harness();
        var toastAppears = new ToastHookFeedback();
        var model = harness.CreateModel(MediaKind.Anime, harness.Dialogs, toastAppears);
        toastAppears.OnToast = () => model.LoadAsync();
        await model.LoadAsync();
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.EditProgress;
        harness.Dialogs.EditProgressAnswer = 7;

        var entry = model.Sections.SelectMany(s => s.AllItems).First(e => e.MediaId == 1);
        await model.ShowActionMenuCommand.ExecuteAsync(entry).WaitAsync(TimeSpan.FromSeconds(5));
        await model.LoadAsync();

        await harness.Inner.Received(1).GetMediaListGroupedAsync(MediaKind.Anime, Arg.Any<CancellationToken>());
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
        gate.SetResult(harness.Groups());
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
            Model = CreateModel(MediaKind.Anime, Dialogs, Feedback);
        }

        /// <summary>What AniList holds for title 1's progress; a change elsewhere moves it.</summary>
        public int TitleOneProgress { get; set; } = 3;

        /// <summary>Whether title 1 is still listed; a removal elsewhere clears it.</summary>
        public bool TitleOneListed { get; set; } = true;

        /// <summary>A fresh copy each read, as AniList gives one.</summary>
        public IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> Groups()
        {
            var watching = new List<MediaListEntry>
            {
                TestDataBuilder.Entry(2, progress: 1, status: MediaListStatus.Current, episodes: 12),
            };
            if (TitleOneListed)
            {
                watching.Insert(0, TestDataBuilder.Entry(1, progress: TitleOneProgress, status: MediaListStatus.Current, episodes: 12));
            }

            return [("Watching", watching)];
        }

        public RecordingUserFeedback Feedback { get; } = new();

        public ManualTimeProvider Time => _time;

        /// <summary>The Anime model's dialogs, scripted per test.</summary>
        public ScriptedDialogService Dialogs { get; } = new();

        public IAniListClient Inner { get; } = Substitute.For<IAniListClient>();

        public ListChangeTracker Tracker { get; } = new();

        /// <summary>What every page resolves as <see cref="IAniListClient"/>.</summary>
        public CachingAniListClient Client { get; }

        public MediaListPageModel Model { get; }

        public MediaListPageModel CreateModel(
            MediaKind kind, ScriptedDialogService? dialogs = null, IUserFeedback? feedback = null)
        {
            dialogs ??= new ScriptedDialogService();
            feedback ??= new RecordingUserFeedback();
            var flow = new ListEntryStatusFlow(dialogs);
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

        /// <summary>Title 1's details page, on the same client and tracker as the Library.</summary>
        public async Task<MediaDetailsPageModel> OpenDetailsAsync()
        {
            var media = new Media { Id = 1, Type = "ANIME", Episodes = 12 };
            var entry = TestDataBuilder.Entry(1, progress: TitleOneProgress, status: MediaListStatus.Current, episodes: 12);
            Inner.GetMediaAsync(1, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<(Media?, MediaListEntry?)>((media, entry)));
            var dialogs = new ScriptedDialogService();
            var details = new MediaDetailsPageModel(
                Client, _auth, new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Substitute.For<INavigationService>(), new RecordingUserFeedback(), new RecordingExternalBrowser(),
                dialogs, new ListEntryStatusFlow(dialogs), Substitute.For<IUpNextOffer>(), Tracker,
                NullLogger<MediaDetailsPageModel>.Instance);
            await details.LoadAsync(1, entry);
            return details;
        }

        public Task AssertSavesAsync(int expected)
            => Inner.Received(expected).SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());

        public Task AssertNothingHeldAsync()
            => Tracker.WaitForIdleAsync(TimeSpan.FromSeconds(5), Time).WaitAsync(TimeSpan.FromSeconds(2));

        /// <summary>Holds the next save open until the test answers it.</summary>
        public TaskCompletionSource<MediaListEntry?> HoldNextSave()
        {
            var save = new TaskCompletionSource<MediaListEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Inner.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>()).Returns(save.Task);
            return save;
        }

        public async Task YieldAsync()
        {
            for (var i = 0; i < 8; i++)
            {
                await Task.Yield();
            }
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

    /// <summary>Feedback whose toast runs a callback, standing in for a popup closing as it shows.</summary>
    private sealed class ToastHookFeedback : IUserFeedback
    {
        public Func<Task>? OnToast { get; set; }

        public Task ShowSnackbarAsync(string message) => Task.CompletedTask;

        public Task ShowSnackbarAsync(string message, string actionText, Action action, TimeSpan? duration = null)
            => Task.CompletedTask;

        public Task ShowToastAsync(string message) => OnToast?.Invoke() ?? Task.CompletedTask;
    }
}