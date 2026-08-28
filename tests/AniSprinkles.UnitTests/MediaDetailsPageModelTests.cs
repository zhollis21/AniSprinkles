using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 1 for <see cref="MediaDetailsPageModel"/>: the <c>LoadAsync</c> state machine and the
/// retry affordance that rides on it.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class MediaDetailsPageModelTests
{
    public MediaDetailsPageModelTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task LoadAsync_HappyPath_ShowsContentAndAllowsRetry()
    {
        var harness = new Harness();
        harness.ReturnsMedia(new Media { Id = 42, Title = new MediaTitle { Romaji = "Frieren" } });

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.Equal(PageState.Content, harness.Model.CurrentState);
        Assert.Equal(42, harness.Model.Media?.Id);
        Assert.True(harness.Model.CanRetry);
    }

    [Fact]
    public async Task IsDescriptionTruncated_UsesTheSharedHeuristic()
    {
        // This page carried its own copy of the estimate, calibrated at 45 chars per line while the
        // shared one moved to 40 for Body2's real 15sp size (#138) — and the label here is identical
        // to the character and staff ones. 340 visible characters is the gap between the two: it
        // fits under the old constant and overflows under the current one, so this fails if the
        // duplicate ever comes back.
        var harness = new Harness();
        harness.ReturnsMedia(new Media { Id = 42, Description = new string('a', 340) });

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.True(harness.Model.IsDescriptionTruncated);
        Assert.Equal(DescriptionTruncationHeuristic.CollapsedMaxLines, harness.Model.DescriptionMaxLines);
    }

    [Fact]
    public async Task LoadAsync_WithANonPositiveMediaId_ErrorsWithoutRetryAndWithoutCallingTheApi()
    {
        // A bad id will still be bad on retry, so the button must not be offered.
        var harness = new Harness();

        await harness.Model.LoadAsync(0, listEntry: null);

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.False(harness.Model.CanRetry);
        await harness.Client.DidNotReceive().GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_WhenTheApiReturnsNoMedia_ErrorsButKeepsRetryAvailable()
    {
        var harness = new Harness();
        harness.ReturnsMedia(null);

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.True(harness.Model.CanRetry);
    }

    [Fact]
    public async Task LoadAsync_WhenTheMediaIsNotFound_HidesRetry()
    {
        // NotFound is AniList telling us the id does not resolve — a dangling relation or a
        // type-constrained lookup. Retrying cannot change that.
        var harness = new Harness();
        harness.Throws(new AniListApiException(ApiErrorKind.NotFound, "no such media"));

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.Equal("Entry Unavailable", harness.Model.ErrorTitle);
        Assert.False(harness.Model.CanRetry);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFetchThrows_ErrorsAndKeepsRetryAvailable()
    {
        var harness = new Harness();
        harness.Throws(new AniListApiException(ApiErrorKind.Network, "offline"));

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.Equal("No Internet Connection", harness.Model.ErrorTitle);
        Assert.True(harness.Model.CanRetry);
    }

    [Fact]
    public async Task LoadAsync_ForTheAlreadyLoadedMedia_ReusesItWithoutASecondRequest()
    {
        // Query attributes get re-applied on resume and back transitions; refetching there would
        // cost a request and a full layout pass for media already on screen.
        var harness = new Harness();
        harness.ReturnsMedia(new Media { Id = 42 });

        await harness.Model.LoadAsync(42, listEntry: null);
        await harness.Model.LoadAsync(42, listEntry: null);

        await harness.Client.Received(1).GetMediaAsync(42, Arg.Any<CancellationToken>());
        Assert.Equal(PageState.Content, harness.Model.CurrentState);
    }

    [Fact]
    public async Task LoadAsync_ForTheAlreadyLoadedMedia_DoesNotOverwriteTheInMemoryListEntry()
    {
        // The in-memory entry reflects saves the user has made since; a stale navigation parameter
        // must not clobber them.
        var harness = new Harness();
        var fresh = new MediaListEntry { Id = 7, MediaId = 42, Progress = 12 };
        harness.ReturnsMedia(new Media { Id = 42 }, fresh);

        await harness.Model.LoadAsync(42, listEntry: null);
        await harness.Model.LoadAsync(42, new MediaListEntry { Id = 7, MediaId = 42, Progress = 1 });

        Assert.Equal(12, harness.Model.ListEntry?.Progress);
    }

    [Fact]
    public async Task RetryLoad_ReInvokesWithTheLastRequestedIdAndItsNavigationListEntry()
    {
        var harness = new Harness();
        harness.Throws(new InvalidOperationException("boom"));

        await harness.Model.LoadAsync(42, new MediaListEntry { Id = 7, MediaId = 42, Progress = 3 });
        Assert.Equal(PageState.Error, harness.Model.CurrentState);

        harness.ReturnsMedia(new Media { Id = 42 });
        await harness.Model.RetryLoadCommand.ExecuteAsync(null);

        Assert.Equal(PageState.Content, harness.Model.CurrentState);
        Assert.Equal(42, harness.Model.Media?.Id);
        // The retry has to carry the navigation entry too, or the retried page loses the list context
        // the user arrived with.
        Assert.Equal(3, harness.Model.ListEntry?.Progress);
    }

    [Fact]
    public async Task RetryLoad_BeforeAnythingHasBeenRequested_DoesNothing()
    {
        var harness = new Harness();
        harness.ReturnsMedia(new Media { Id = 42 });

        await harness.Model.RetryLoadCommand.ExecuteAsync(null);

        await harness.Client.DidNotReceive().GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_WhileALoadIsAlreadyInFlight_IsDropped()
    {
        var harness = new Harness();
        var gate = new TaskCompletionSource<(Media?, MediaListEntry?)>();
        harness.Client.GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(gate.Task);

        var first = harness.Model.LoadAsync(42, listEntry: null);
        // Unlike the other three details pages, a second load does not supersede the first: this load is
        // the heavy one and its list-entry merge is order-sensitive.
        await harness.Model.LoadAsync(43, listEntry: null);

        gate.SetResult((new Media { Id = 42 }, null));
        await first;

        Assert.Equal(42, harness.Model.Media?.Id);
        await harness.Client.Received(1).GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_WhenASecondLoadIsDropped_DoesNotHandItsListEntryToTheLoadStillRunning()
    {
        var harness = new Harness();
        var gate = new TaskCompletionSource<(Media?, MediaListEntry?)>();
        harness.Client.GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(gate.Task);

        var live = harness.Model.LoadAsync(42, new MediaListEntry { Id = 7, MediaId = 42, Progress = 3 });

        // Dropped at the in-flight guard. It must not leave its list entry behind for the live load to
        // pick up in SeedSections — that would show the wrong progress for the title being loaded.
        await harness.Model.LoadAsync(99, new MediaListEntry { Id = 8, MediaId = 99, Progress = 99 });

        gate.SetResult((new Media { Id = 42 }, null));
        await live;

        Assert.Equal(3, harness.Model.ListEntry?.Progress);
        Assert.Equal(7, harness.Model.ListEntry?.Id);
    }

    [Fact]
    public async Task RetryLoad_AfterASecondLoadWasDropped_ReusesTheLiveLoadsListEntry()
    {
        var harness = new Harness();
        harness.Throws(new InvalidOperationException("boom"));
        await harness.Model.LoadAsync(42, new MediaListEntry { Id = 7, MediaId = 42, Progress = 3 });

        // Dropped: a load is not in flight, but the failed one already set the retry context.
        await harness.Model.LoadAsync(99, new MediaListEntry { Id = 8, MediaId = 99, Progress = 99 });

        harness.ReturnsMedia(new Media { Id = 99 });
        await harness.Model.RetryLoadCommand.ExecuteAsync(null);

        // The second load was accepted (nothing was in flight), so retry follows it, not the first.
        Assert.Equal(99, harness.Model.Media?.Id);
        Assert.Equal(99, harness.Model.ListEntry?.Progress);
    }

    [Fact]
    public async Task LoadAsync_WhenASecondLoadIsDropped_DoesNotRenumberTheLiveLoadsTraceLines()
    {
        var harness = new Harness();
        var gate = new TaskCompletionSource<(Media?, MediaListEntry?)>();
        harness.Client.GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(gate.Task);

        var live = harness.Model.LoadAsync(42, listEntry: null);
        await harness.Model.LoadAsync(99, listEntry: null);

        gate.SetResult((new Media { Id = 42 }, null));
        await live;

        // load#1 is the live load; load#2 was dropped. Everything the live load emits after the drop
        // must still say load#1, or the correlation id is worse than useless when reading a trace.
        Assert.Contains(harness.Logger.Containing("skipped because"), m => m.Contains("load#2", StringComparison.Ordinal));
        Assert.All(
            harness.Logger.Containing("DATATRACE"),
            m => Assert.Contains("load#1", m, StringComparison.Ordinal));
        Assert.Contains(harness.Logger.Containing("media fetch completed"), m => m.Contains("load#1", StringComparison.Ordinal));
    }

    private sealed class Harness
    {
        public Harness()
        {
            var dialogs = new ScriptedDialogService();
            Model = new MediaDetailsPageModel(
                Client,
                Substitute.For<IAuthService>(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Substitute.For<INavigationService>(),
                new RecordingUserFeedback(),
                new RecordingExternalBrowser(),
                dialogs,
                new ListEntryStatusFlow(dialogs),
                Logger);
        }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public RecordingLogger<MediaDetailsPageModel> Logger { get; } = new();

        public MediaDetailsPageModel Model { get; }

        public void ReturnsMedia(Media? media, MediaListEntry? listEntry = null)
            => Client.GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult((media, listEntry)));

        public void Throws(Exception exception)
            => Client.GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<(Media?, MediaListEntry?)>(exception));
    }
}
