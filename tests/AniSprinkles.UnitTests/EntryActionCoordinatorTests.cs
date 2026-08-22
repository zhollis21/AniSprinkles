using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The shared long-press flows — action menu, move/add/rate/edit-progress/remove, persistence,
/// optimistic UI and rollback. One instance per page model, and every list surface (My Anime,
/// Discover, Search, View All) routes through it, so a defect here shows up in four places at once.
///
/// Untestable before #62: every entry point opened a CommunityToolkit popup through
/// <c>Shell.Current</c>, which is null rather than throwing off-device — so the whole flow would
/// silently no-op and any test of it would pass without executing a single decision.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class EntryActionCoordinatorTests
{
    public EntryActionCoordinatorTests() => TestDataBuilder.ResetAppSettings();

    // ── Action menu routing ──────────────────────────────────────────

    [Fact]
    public async Task ShowEntryMenu_WhenDismissed_RunsNoFlow()
    {
        var harness = new Harness();
        harness.Dialogs.EntryActionAnswer = null;

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        Assert.Equal([nameof(IDialogService.ShowEntryActionsAsync)], harness.Dialogs.Calls);
        await harness.Client.DidNotReceive().SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowEntryMenu_FlushesPendingWorkBeforeOpening()
    {
        // My Anime hangs its debounced +1 flush off OnBeforeFlowAsync. If the menu opened first, the
        // pending increment would land underneath whatever the user picked.
        var order = new List<string>();
        var harness = new Harness(onBeforeFlow: () => { order.Add("flush"); return Task.CompletedTask; });
        harness.Dialogs.OnCall = call => order.Add(call);

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        Assert.Equal(["flush", nameof(IDialogService.ShowEntryActionsAsync)], order);
    }

    [Fact]
    public async Task ShowEntryMenu_OpenDetails_DelegatesToTheHostWithoutSaving()
    {
        var harness = new Harness();
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.OpenDetails;

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        Assert.Equal([harness.Entry], harness.OpenedDetails);
        await harness.Client.DidNotReceive().SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
    }

    // ── Move to list ─────────────────────────────────────────────────

    [Fact]
    public async Task MoveToList_ChoosingRemove_RunsTheDeleteFlowRatherThanAMove()
    {
        // The sheet's remove row and its status rows come back through one result. Routing the
        // remove branch into HandleMove would "move" the entry to a status it never picked.
        var harness = new Harness();
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.MoveToList;
        harness.Dialogs.MoveToListAnswer = MoveToListChoice.Remove;
        harness.Dialogs.ConfirmAnswer = true;
        harness.Client.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        await harness.Client.Received(1).DeleteMediaListEntryAsync(harness.Entry.Id, Arg.Any<CancellationToken>());
        await harness.Client.DidNotReceive().SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
        Assert.Equal([harness.Entry], harness.Removed);
    }

    [Fact]
    public async Task MoveToList_ChoosingAStatus_SavesUnderThatStatus()
    {
        var harness = new Harness();
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.MoveToList;
        harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(MediaListStatus.Paused);
        harness.SaveEchoesBack();

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        Assert.Equal(MediaListStatus.Paused, harness.Entry.Status);
        Assert.Equal([harness.Entry], harness.StatusChanged);
        Assert.Contains(harness.Feedback.Toasts, t => t.Contains("moved to Paused"));
    }

    [Fact]
    public async Task MoveToList_WhenTheSaveFails_RollsTheEntryBackAndTellsTheHost()
    {
        // The move mutates status/progress/score/repeat before the round trip, and the host has
        // already dropped the row optimistically. A failure has to undo both.
        var harness = new Harness();
        var entry = harness.Entry;
        entry.Progress = 5;
        entry.Score = 7;
        entry.Repeat = 2;

        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.MoveToList;
        harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(MediaListStatus.Completed);
        harness.Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MediaListEntry?>(new AniListApiException(ApiErrorKind.Network, "offline")));

        await harness.Coordinator.ShowEntryMenuAsync(entry);

        Assert.Equal(MediaListStatus.Current, entry.Status);
        Assert.Equal(5, entry.Progress);
        Assert.Equal(7, entry.Score);
        Assert.Equal(2, entry.Repeat);
        Assert.Equal(1, harness.MutationFailures);

        // A move's side effects were just reverted, so there is no coherent state to retry from —
        // the user long-presses again. Only a ServiceOutage swaps in the outage title.
        Assert.Equal("Failed to move. Please try again.", Assert.Single(harness.Feedback.Snackbars));
        Assert.Null(harness.Feedback.LastSnackbarAction);
    }

    // ── Remove ───────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_WhenTheUserCancelsTheConfirmation_DeletesNothing()
    {
        var harness = new Harness();
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.Remove;
        harness.Dialogs.ConfirmAnswer = false;

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        await harness.Client.DidNotReceive().DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.Empty(harness.OptimisticallyRemoved);
    }

    [Fact]
    public async Task Remove_WhenTheDeleteFails_OffersRetryThatReissuesTheSameDelete()
    {
        var harness = new Harness();
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.Remove;
        harness.Dialogs.ConfirmAnswer = true;
        harness.Client.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new AniListApiException(ApiErrorKind.Network, "offline")));

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        Assert.Equal(1, harness.MutationFailures);
        Assert.NotNull(harness.Feedback.LastSnackbarAction);

        // The retry must target the id captured at failure time, not whatever the entry holds later.
        harness.Client.ClearReceivedCalls();
        harness.Client.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        harness.Feedback.LastSnackbarAction!();
        await harness.WaitUntilAsync(() => harness.Removed.Count == 1);

        await harness.Client.Received(1).DeleteMediaListEntryAsync(harness.Entry.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_WhenTheRetryItselfFailsIntoAnOutage_StopsOfferingRetry()
    {
        // The retry path can fail again, and it re-enters the same failure handler. Once the failure
        // becomes an outage the chain has to stop offering a button that cannot work — otherwise the
        // user is invited to hammer a dead API indefinitely.
        var harness = new Harness();
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.Remove;
        harness.Dialogs.ConfirmAnswer = true;
        harness.Client.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new AniListApiException(ApiErrorKind.Network, "offline")));

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);
        Assert.NotNull(harness.Feedback.LastSnackbarAction);

        // The retry runs while AniList is down.
        harness.Client.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new AniListApiException(ApiErrorKind.ServiceOutage, "down")));
        harness.Feedback.LastSnackbarAction!();
        await harness.WaitUntilAsync(() => harness.Feedback.Snackbars.Count == 2);

        Assert.Equal("AniList is Down", harness.Feedback.Snackbars[1]);
        Assert.Null(harness.Feedback.LastSnackbarAction);
    }

    [Fact]
    public async Task Remove_DuringAServiceOutage_OmitsTheRetryAction()
    {
        // The outage banner is already up and a retry cannot succeed for minutes; offering Retry
        // invites the user to hammer a dead API.
        var harness = new Harness();
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.Remove;
        harness.Dialogs.ConfirmAnswer = true;
        harness.Client.DeleteMediaListEntryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new AniListApiException(ApiErrorKind.ServiceOutage, "down")));

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        Assert.Contains("AniList is Down", harness.Feedback.Snackbars);
        Assert.Null(harness.Feedback.LastSnackbarAction);
    }

    // ── Add to list ──────────────────────────────────────────────────

    [Fact]
    public async Task AddToList_AdoptsTheServerAssignedEntryId()
    {
        // A candidate starts at Id 0. Without adopting the server's id, a later Remove would delete
        // entry 0 — i.e. nothing, while the UI claims success.
        var harness = new Harness();
        var candidate = TestDataBuilder.Entry(mediaId: 99, status: null);
        candidate.Id = 0;

        harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(MediaListStatus.Planning);
        harness.Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns(new MediaListEntry { Id = 4242 });

        await harness.Coordinator.ShowAddToListAsync(candidate);

        Assert.Equal(4242, candidate.Id);
        Assert.Equal([candidate], harness.StatusChanged);
    }

    [Fact]
    public async Task AddToList_WhenTheServerReturnsNoId_FailsLoudlyInsteadOfShowingSuccess()
    {
        // A create that yields no id did not really take. Reporting success would leave an entry the
        // user believes is on their list and cannot subsequently remove.
        var harness = new Harness();
        var candidate = TestDataBuilder.Entry(mediaId: 99, status: null);
        candidate.Id = 0;

        harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(MediaListStatus.Planning);
        harness.Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns((MediaListEntry?)null);

        await harness.Coordinator.ShowAddToListAsync(candidate);

        Assert.Empty(harness.StatusChanged);
        Assert.Empty(harness.Feedback.Toasts);
        Assert.Contains("Failed to add. Please try again.", harness.Feedback.Snackbars);
    }

    // ── Edit progress ────────────────────────────────────────────────

    [Fact]
    public async Task EditProgress_ClampsAboveTheEpisodeCap()
    {
        var harness = new Harness(episodes: 12);
        harness.Entry.Progress = 3;
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.EditProgress;
        harness.Dialogs.EditProgressAnswer = 9999;
        harness.Dialogs.ConfirmAnswer = false; // reaching the cap opens the completion confirm
        harness.SaveEchoesBack();

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        // Clamped to 12, which is the cap, so it routes into the completion flow — and the user
        // declined, so nothing is saved and progress is untouched.
        Assert.Contains(nameof(IDialogService.ConfirmAsync), harness.Dialogs.Calls);
        Assert.Equal(3, harness.Entry.Progress);
    }

    [Fact]
    public async Task EditProgress_ToTheSameValue_SavesNothing()
    {
        var harness = new Harness(episodes: 12);
        harness.Entry.Progress = 4;
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.EditProgress;
        harness.Dialogs.EditProgressAnswer = 4;

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        await harness.Client.DidNotReceive().SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditProgress_WhenTheSaveFails_RestoresThePreviousProgress()
    {
        var harness = new Harness(episodes: 12);
        harness.Entry.Progress = 4;
        harness.Dialogs.EntryActionAnswer = MyAnimeEntryAction.EditProgress;
        harness.Dialogs.EditProgressAnswer = 6;
        harness.Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MediaListEntry?>(new InvalidOperationException("boom")));

        await harness.Coordinator.ShowEntryMenuAsync(harness.Entry);

        Assert.Equal(4, harness.Entry.Progress);
    }

    // ── Completion flow ──────────────────────────────────────────────

    [Fact]
    public async Task CompletionFlow_WhenConfirmed_CompletesAndSaves()
    {
        var harness = new Harness(episodes: 12);
        harness.Entry.Progress = 11;
        harness.Dialogs.ConfirmAnswer = true;
        harness.Dialogs.RatingAnswer = 9;
        harness.SaveEchoesBack();

        await harness.Coordinator.RunCompletionFlowAsync(harness.Entry);

        Assert.Equal(MediaListStatus.Completed, harness.Entry.Status);
        Assert.Equal(12, harness.Entry.Progress);
        Assert.Equal(9, harness.Entry.Score);
        Assert.Equal([harness.Entry], harness.StatusChanged);
    }

    [Fact]
    public async Task CompletionFlow_WhenDeclined_ChangesNothing()
    {
        var harness = new Harness(episodes: 12);
        harness.Entry.Progress = 11;
        harness.Dialogs.ConfirmAnswer = false;

        await harness.Coordinator.RunCompletionFlowAsync(harness.Entry);

        Assert.Equal(MediaListStatus.Current, harness.Entry.Status);
        Assert.Equal(11, harness.Entry.Progress);
        await harness.Client.DidNotReceive().SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompletionFlow_WhenAlreadyRunning_DoesNotStartASecondTime()
    {
        // A +1 tap that reaches the cap and a "Mark as completed" menu pick can both land. Without
        // the guard the user gets two stacked confirmations and two saves.
        var harness = new Harness(episodes: 12);
        harness.Entry.Progress = 11;
        harness.SaveEchoesBack();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.ConfirmAnswer = true;
        harness.Dialogs.BeforeConfirmAsync = async () =>
        {
            opened.TrySetResult();
            await gate.Task;
        };

        var first = harness.Coordinator.RunCompletionFlowAsync(harness.Entry);
        await opened.Task;

        // Bounded on purpose: without the guard the second call reaches the same held-open
        // confirmation and blocks on a gate this test only releases afterwards, so an unbounded
        // await would deadlock the suite instead of failing it.
        await harness.Coordinator.RunCompletionFlowAsync(harness.Entry)
            .WaitAsync(TimeSpan.FromSeconds(5));

        gate.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, harness.Dialogs.Calls.Count(c => c == nameof(IDialogService.ConfirmAsync)));
        await harness.Client.Received(1).SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompletionFlow_WhenTheScorePromptIsSkipped_StillCompletes()
    {
        // Skipping the rating means "leave the score alone", not "cancel the completion".
        var harness = new Harness(episodes: 12);
        harness.Entry.Progress = 11;
        harness.Entry.Score = 6;
        harness.Dialogs.ConfirmAnswer = true;
        harness.Dialogs.RatingAnswer = null;
        harness.SaveEchoesBack();

        await harness.Coordinator.RunCompletionFlowAsync(harness.Entry);

        Assert.Equal(MediaListStatus.Completed, harness.Entry.Status);
        Assert.Equal(6, harness.Entry.Score);
    }

    private sealed class Harness
    {
        private int _mutationFailures;

        public Harness(int? episodes = null, Func<Task>? onBeforeFlow = null)
        {
            Entry = TestDataBuilder.Entry(mediaId: 1, episodes: episodes);

            Host = new EntryActionHost
            {
                OnBeforeFlowAsync = onBeforeFlow,
                OpenDetailsAsync = e => { OpenedDetails.Add(e); return Task.CompletedTask; },
                OnOptimisticRemove = e => OptimisticallyRemoved.Add(e),
                OnEntrySavedInPlaceAsync = e => { SavedInPlace.Add(e); return Task.CompletedTask; },
                OnEntryStatusChangedAsync = e => { StatusChanged.Add(e); return Task.CompletedTask; },
                OnEntryRemovedAsync = e => { Removed.Add(e); return Task.CompletedTask; },
                OnMutationFailedAsync = () => { Interlocked.Increment(ref _mutationFailures); return Task.CompletedTask; },
                SetErrorDetails = d => ErrorDetails = d,
            };

            Coordinator = new EntryActionCoordinator(
                Client,
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Dialogs,
                Feedback,
                new ListEntryStatusFlow(Dialogs),
                NullLogger.Instance,
                Host);
        }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public ScriptedDialogService Dialogs { get; } = new();

        public RecordingUserFeedback Feedback { get; } = new();

        public EntryActionHost Host { get; }

        public EntryActionCoordinator Coordinator { get; }

        public MediaListEntry Entry { get; }

        public List<MediaListEntry> OpenedDetails { get; } = [];

        public List<MediaListEntry> OptimisticallyRemoved { get; } = [];

        public List<MediaListEntry> SavedInPlace { get; } = [];

        public List<MediaListEntry> StatusChanged { get; } = [];

        public List<MediaListEntry> Removed { get; } = [];

        public string? ErrorDetails { get; private set; }

        public int MutationFailures => Volatile.Read(ref _mutationFailures);

        /// <summary>A save that succeeds and returns the entry's own id, as AniList does.</summary>
        public void SaveEchoesBack()
            => Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<MediaListEntry?>(
                    new MediaListEntry { Id = call.Arg<MediaListEntry>().Id is var id and > 0 ? id : 1 }));

        /// <summary>Retry actions are fired as un-awaited continuations, so poll for the effect.</summary>
        public async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(1);
            }

            Assert.True(condition(), "Timed out waiting for the retry to settle.");
        }
    }
}
