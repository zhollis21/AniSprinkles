using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The +1 episode flow: an optimistic progress bump batched behind a 1500 ms debounce so a user
/// catching up on six episodes spends one AniList write instead of six. The batching, the
/// revert-to-before-the-first-tap on failure, and the hand-off to the completion flow are all
/// decisions with no visible artifact until they go wrong.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class MediaListPageModelTests
{
    private static readonly TimeSpan PastTheDebounce = TimeSpan.FromMilliseconds(1600);

    public MediaListPageModelTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task IncrementProgress_UpdatesTheUiBeforeTheSaveLands()
    {
        // The pill has to feel instant; the write is what waits.
        var harness = new Harness();
        var entry = harness.Watching(progress: 3);

        var increment = harness.Model.IncrementProgressCommand.ExecuteAsync(entry);

        Assert.Equal(4, entry.Progress);
        await harness.Client.DidNotReceive().SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());

        await harness.AdvancePastDebounceAsync();
        await increment;
    }

    [Fact]
    public async Task IncrementProgress_RapidTaps_CollapseIntoASingleSaveAtTheFinalValue()
    {
        // Six taps must not be six writes — this is the app's largest single rate-limit saving.
        var harness = new Harness();
        var entry = harness.Watching(progress: 0);
        harness.SaveEchoesBack();

        var taps = new List<Task>();
        for (var i = 0; i < 6; i++)
        {
            taps.Add(harness.Model.IncrementProgressCommand.ExecuteAsync(entry));
        }

        Assert.Equal(6, entry.Progress);

        await harness.AdvancePastDebounceAsync();
        await Task.WhenAll(taps);

        await harness.Client.Received(1).SaveMediaListEntryAsync(
            Arg.Is<MediaListEntry>(e => e.Progress == 6), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncrementProgress_WhenTheSaveFails_RevertsToBeforeTheFirstTapOfTheSeries()
    {
        // Reverting one step would strand the other five. The pre-increment snapshot is taken once
        // per debounce series precisely so the whole batch can be undone.
        var harness = new Harness();
        var entry = harness.Watching(progress: 2);
        harness.Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MediaListEntry?>(new AniListApiException(ApiErrorKind.Network, "offline")));

        var taps = new List<Task>();
        for (var i = 0; i < 3; i++)
        {
            taps.Add(harness.Model.IncrementProgressCommand.ExecuteAsync(entry));
        }

        Assert.Equal(5, entry.Progress);

        await harness.AdvancePastDebounceAsync();
        await Task.WhenAll(taps);

        Assert.Equal(2, entry.Progress);
        Assert.NotEmpty(harness.Feedback.Snackbars);
    }

    [Fact]
    public async Task IncrementProgress_SwitchingEntriesMidDebounce_FlushesThePreviousEntryFirst()
    {
        // Only one pending increment is tracked. Without the flush, tapping +1 on a second card
        // would silently discard the first card's un-saved progress.
        var harness = new Harness();
        var first = harness.Watching(progress: 1, mediaId: 1);
        var second = harness.Watching(progress: 8, mediaId: 2);
        harness.SaveEchoesBack();

        var firstTap = harness.Model.IncrementProgressCommand.ExecuteAsync(first);
        var secondTap = harness.Model.IncrementProgressCommand.ExecuteAsync(second);

        // The first entry was saved immediately by the flush, before any debounce elapsed.
        await harness.Client.Received(1).SaveMediaListEntryAsync(
            Arg.Is<MediaListEntry>(e => e.MediaId == 1 && e.Progress == 2), Arg.Any<CancellationToken>());

        await harness.AdvancePastDebounceAsync();
        await Task.WhenAll(firstTap, secondTap);

        await harness.Client.Received(1).SaveMediaListEntryAsync(
            Arg.Is<MediaListEntry>(e => e.MediaId == 2 && e.Progress == 9), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncrementProgress_ReachingTheKnownTotal_RoutesToTheCompletionFlowNotTheDebounce()
    {
        // Finishing a show should offer the completion confirm and rating, and save immediately —
        // waiting 1500 ms behind a debounce would let the user navigate away mid-celebration.
        var harness = new Harness();
        var entry = harness.Watching(progress: 11, episodes: 12);
        harness.Dialogs.ConfirmAnswer = true;
        harness.Dialogs.RatingAnswer = 8;
        harness.SaveEchoesBack();

        // Bounded on purpose. If the routing regresses, this tap falls through to the +1 debounce
        // instead — and that waits on a ManualTimeProvider the test never advances, so an unbounded
        // await would hang the suite rather than failing it.
        await harness.Model.IncrementProgressCommand
            .ExecuteAsync(entry)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Contains(nameof(IDialogService.ConfirmAsync), harness.Dialogs.Calls);
        Assert.Equal(MediaListStatus.Completed, entry.Status);
        Assert.Equal(12, entry.Progress);
        await harness.Client.Received(1).SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncrementProgress_WhenAlreadyCaughtUp_SaysSoAndSavesNothing()
    {
        // The pill is dimmed but still takes taps; silence would read as a broken button.
        var harness = new Harness();
        var entry = harness.Watching(progress: 5, nextAiringEpisode: 6);

        await harness.Model.IncrementProgressCommand.ExecuteAsync(entry);

        Assert.Contains("You're caught up", harness.Feedback.Toasts);
        Assert.Equal(5, entry.Progress);
        await harness.Client.DidNotReceive().SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);

        public Harness()
        {
            var preferences = Substitute.For<IPreferences>();
            preferences.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(c => c.ArgAt<string>(1));
            preferences.Get(Arg.Any<string>(), Arg.Any<bool>()).Returns(c => c.ArgAt<bool>(1));
            preferences.Get(Arg.Any<string>(), Arg.Any<int>()).Returns(c => c.ArgAt<int>(1));

            Model = new AnimeLibraryPageModel(
                Client,
                Substitute.For<IAuthService>(),
                Substitute.For<IAiringNotificationService>(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                preferences,
                Substitute.For<INavigationService>(),
                Dialogs,
                Feedback,
                new ListEntryStatusFlow(Dialogs),
                _time,
                NullLogger<AnimeLibraryPageModel>.Instance);
        }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public ScriptedDialogService Dialogs { get; } = new();

        public RecordingUserFeedback Feedback { get; } = new();

        public AnimeLibraryPageModel Model { get; }

        public MediaListEntry Watching(int progress, int mediaId = 1, int? episodes = null, int? nextAiringEpisode = null)
            => TestDataBuilder.Entry(
                mediaId,
                progress: progress,
                status: MediaListStatus.Current,
                episodes: episodes,
                nextAiringEpisode: nextAiringEpisode);

        public void SaveEchoesBack()
            => Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<MediaListEntry?>(new MediaListEntry { Id = call.Arg<MediaListEntry>().Id }));

        /// <summary>
        /// Lets the fire-and-forget debounce register its timer, then moves the clock past it.
        /// </summary>
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
