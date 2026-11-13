using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 1 for <see cref="MediaDetailsPageModel"/>: the <c>LoadAsync</c> state machine and the
/// retry affordance that rides on it.
/// </summary>
[NonParallelizable]
public class MediaDetailsPageModelTests
{
    public MediaDetailsPageModelTests() => TestDataBuilder.ResetAppSettings();

    [Test]
    public async Task LoadAsync_HappyPath_ShowsContentAndAllowsRetry()
    {
        var harness = new Harness();
        harness.ReturnsMedia(new Media { Id = 42, Title = new MediaTitle { Romaji = "Frieren" } });

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Content));
        Assert.That(harness.Model.Media?.Id, Is.EqualTo(42));
        Assert.That(harness.Model.CanRetry, Is.True);
    }

    [Test]
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

        Assert.That(harness.Model.IsDescriptionTruncated, Is.True);
        Assert.That(harness.Model.DescriptionMaxLines, Is.EqualTo(DescriptionTruncationHeuristic.CollapsedMaxLines));
    }

    [Test]
    public async Task LoadAsync_WithANonPositiveMediaId_ErrorsWithoutRetryAndWithoutCallingTheApi()
    {
        // A bad id will still be bad on retry, so the button must not be offered.
        var harness = new Harness();

        await harness.Model.LoadAsync(0, listEntry: null);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Error));
        Assert.That(harness.Model.CanRetry, Is.False);
        await harness.Client.DidNotReceive().GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAsync_WhenTheApiReturnsNoMedia_ErrorsButKeepsRetryAvailable()
    {
        var harness = new Harness();
        harness.ReturnsMedia(null);

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Error));
        Assert.That(harness.Model.CanRetry, Is.True);
    }

    [Test]
    public async Task LoadAsync_WhenTheMediaIsNotFound_KeepsTheWordingButStillOffersRetry()
    {
        // NotFound is usually AniList telling us the id does not resolve — a dangling relation or a
        // type-constrained lookup — and retrying usually cannot change that. It is offered anyway
        // (#158), because we could not previously distinguish that case from a transient failure
        // misfiled as one, and the user paid for the ambiguity with a dead page.
        var harness = new Harness();
        harness.Throws(new AniListApiException(ApiErrorKind.NotFound, "no such media"));

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Error));
        Assert.That(harness.Model.ErrorTitle, Is.EqualTo("Entry Unavailable"));
        Assert.That(harness.Model.CanRetry, Is.True);
        Assert.That(harness.Model.ErrorDetails, Is.Not.EqualTo(string.Empty));
    }

    [Test]
    public async Task LoadAsync_WhenTheFetchThrows_ErrorsAndKeepsRetryAvailable()
    {
        var harness = new Harness();
        harness.Throws(new AniListApiException(ApiErrorKind.Network, "offline"));

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Error));
        Assert.That(harness.Model.ErrorTitle, Is.EqualTo("No Internet Connection"));
        Assert.That(harness.Model.CanRetry, Is.True);
    }

    [Test]
    public async Task LoadAsync_ForTheAlreadyLoadedMedia_ReusesItWithoutASecondRequest()
    {
        // Query attributes get re-applied on resume and back transitions; refetching there would
        // cost a request and a full layout pass for media already on screen.
        var harness = new Harness();
        harness.ReturnsMedia(new Media { Id = 42 });

        await harness.Model.LoadAsync(42, listEntry: null);
        await harness.Model.LoadAsync(42, listEntry: null);

        await harness.Client.Received(1).GetMediaAsync(42, Arg.Any<CancellationToken>());
        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Content));
    }

    [Test]
    public async Task LoadAsync_ForTheAlreadyLoadedMedia_DoesNotOverwriteTheInMemoryListEntry()
    {
        // The in-memory entry reflects saves the user has made since; a stale navigation parameter
        // must not clobber them.
        var harness = new Harness();
        var fresh = new MediaListEntry { Id = 7, MediaId = 42, Progress = 12 };
        harness.ReturnsMedia(new Media { Id = 42 }, fresh);

        await harness.Model.LoadAsync(42, listEntry: null);
        await harness.Model.LoadAsync(42, new MediaListEntry { Id = 7, MediaId = 42, Progress = 1 });

        Assert.That(harness.Model.ListEntry?.Progress, Is.EqualTo(12));
    }

    [Test]
    public async Task RetryLoad_ReInvokesWithTheLastRequestedIdAndItsNavigationListEntry()
    {
        var harness = new Harness();
        harness.Throws(new InvalidOperationException("boom"));

        await harness.Model.LoadAsync(42, new MediaListEntry { Id = 7, MediaId = 42, Progress = 3 });
        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Error));

        harness.ReturnsMedia(new Media { Id = 42 });
        await harness.Model.RetryLoadCommand.ExecuteAsync(null);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Content));
        Assert.That(harness.Model.Media?.Id, Is.EqualTo(42));
        // The retry has to carry the navigation entry too, or the retried page loses the list context
        // the user arrived with.
        Assert.That(harness.Model.ListEntry?.Progress, Is.EqualTo(3));
    }

    [Test]
    public async Task RetryLoad_BeforeAnythingHasBeenRequested_DoesNothing()
    {
        var harness = new Harness();
        harness.ReturnsMedia(new Media { Id = 42 });

        await harness.Model.RetryLoadCommand.ExecuteAsync(null);

        await harness.Client.DidNotReceive().GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
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

        Assert.That(harness.Model.Media?.Id, Is.EqualTo(42));
        await harness.Client.Received(1).GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
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

        Assert.That(harness.Model.ListEntry?.Progress, Is.EqualTo(3));
        Assert.That(harness.Model.ListEntry?.Id, Is.EqualTo(7));
    }

    [Test]
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
        Assert.That(harness.Model.Media?.Id, Is.EqualTo(99));
        Assert.That(harness.Model.ListEntry?.Progress, Is.EqualTo(99));
    }

    [Test]
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
        Assert.That(harness.Logger.Containing("skipped because"), Has.Some.Contains("load#2"));
        Assert.Multiple(() =>
        {
            foreach (var m in harness.Logger.Containing("DATATRACE"))
            {
                Assert.That(m, Does.Contain("load#1"));
            }
        });
        Assert.That(harness.Logger.Containing("media fetch completed"), Has.Some.Contains("load#1"));
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

    // ── Manga (#12) ──────────────────────────────────────────────────

    [Test]
    public async Task AMangaPage_LabelsEveryStatusInTheReadingVocabulary()
    {
        var harness = new Harness();
        harness.ReturnsMedia(MangaMedia(chapters: 141, volumes: 34));

        await harness.Model.LoadAsync(53390, listEntry: null);

        Assert.That(harness.Model.CurrentMediaKind, Is.EqualTo(MediaKind.Manga));
        Assert.That(harness.Model.StatusLabelCurrent, Is.EqualTo("Reading"));
        Assert.That(harness.Model.StatusLabelPlanning, Is.EqualTo("Plan to Read"));
        Assert.That(harness.Model.StatusLabelRepeating, Is.EqualTo("Rereading"));
    }

    [Test]
    public async Task AnAnimePage_KeepsTheWatchingVocabulary()
    {
        var harness = new Harness();
        harness.ReturnsMedia(new Media { Id = 42, Type = "ANIME", Episodes = 12 });

        await harness.Model.LoadAsync(42, listEntry: null);

        Assert.That(harness.Model.CurrentMediaKind, Is.EqualTo(MediaKind.Anime));
        Assert.That(harness.Model.StatusLabelCurrent, Is.EqualTo("Watching"));
        Assert.That(harness.Model.StatusLabelPlanning, Is.EqualTo("Plan to Watch"));
        Assert.That(harness.Model.StatusLabelRepeating, Is.EqualTo("Rewatching"));
        Assert.That(harness.Model.CurrentProgressUnit, Is.EqualTo(MediaProgressUnit.Episode));
        Assert.That(harness.Model.ProgressSectionLabel, Is.EqualTo("Episodes watched"));
    }

    [Test]
    public async Task AMangaChipRow_ShowsChaptersAndVolumesInsteadOfEpisodesAndSeason()
    {
        var harness = new Harness();
        harness.ReturnsMedia(MangaMedia(chapters: 141, volumes: 34));

        await harness.Model.LoadAsync(53390, listEntry: null);

        Assert.That(harness.Model.ChaptersDisplay, Is.EqualTo("141 Chapters"));
        Assert.That(harness.Model.VolumesDisplay, Is.EqualTo("34 Volumes"));

        // One-shots make the singular a visible chip rather than a theoretical one.
        harness.Model.Media = MangaMedia(chapters: 1, volumes: null);
        Assert.That(harness.Model.ChaptersDisplay, Is.EqualTo("1 Chapter"));
        Assert.That(harness.Model.VolumesDisplay, Is.EqualTo(string.Empty));
        // The anime chips gate on their own emptiness, so they hide themselves for manga.
        Assert.That(harness.Model.EpisodesDisplay, Is.EqualTo(string.Empty));
        Assert.That(harness.Model.DurationPillDisplay, Is.EqualTo(string.Empty));
        Assert.That(harness.Model.SeasonYearDisplay, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task AMangaChapterReader_GetsAChapterProgressControl()
    {
        var harness = new Harness();
        var entry = new MediaListEntry { Id = 1, MediaId = 53390, Progress = 100, Status = MediaListStatus.Current };
        harness.ReturnsMedia(MangaMedia(chapters: 141, volumes: 34), entry);

        await harness.Model.LoadAsync(53390, entry);

        Assert.That(harness.Model.CurrentProgressUnit, Is.EqualTo(MediaProgressUnit.Chapter));
        Assert.That(harness.Model.ProgressUnitNoun, Is.EqualTo("Chapter"));
        // The row heading is the only thing on the page that says WHICH counter the number and the
        // slider refer to — the unit is decided per entry, so a bare "Progress" was ambiguous.
        Assert.That(harness.Model.ProgressSectionLabel, Is.EqualTo("Chapters read"));
        Assert.That(harness.Model.ProgressLabel, Is.EqualTo("100 / 141"));
        Assert.That(harness.Model.ProgressSliderMax, Is.EqualTo(141));
    }

    [Test]
    public async Task AMangaVolumeReader_GetsAVolumeProgressControl()
    {
        var harness = new Harness();
        var entry = new MediaListEntry
        {
            Id = 1, MediaId = 53390, Progress = 0, ProgressVolumes = 20, Status = MediaListStatus.Current,
        };
        harness.ReturnsMedia(MangaMedia(chapters: 141, volumes: 34), entry);

        await harness.Model.LoadAsync(53390, entry);

        Assert.That(harness.Model.CurrentProgressUnit, Is.EqualTo(MediaProgressUnit.Volume));
        Assert.That(harness.Model.ProgressUnitNoun, Is.EqualTo("Volume"));
        Assert.That(harness.Model.ProgressSectionLabel, Is.EqualTo("Volumes read"));
        Assert.That(harness.Model.ProgressLabel, Is.EqualTo("20 / 34"));
        Assert.That(harness.Model.ProgressSliderMax, Is.EqualTo(34));
    }

    [Test]
    public async Task AnOngoingManga_HasNoProgressBarBecauseAniListPublishesNoTotal()
    {
        // Not an edge case: AniList returns null chapters and null volumes for every RELEASING
        // series, and manga has no nextAiringEpisode to stand in for a cap the way an airing anime
        // does. So the bar is hidden and the label is a bare count.
        var harness = new Harness();
        var entry = new MediaListEntry { Id = 1, MediaId = 30013, Progress = 1100, Status = MediaListStatus.Current };
        harness.ReturnsMedia(MangaMedia(chapters: null, volumes: null), entry);

        await harness.Model.LoadAsync(30013, entry);

        Assert.That(harness.Model.HasProgressSliderMax, Is.False);
        Assert.That(harness.Model.ProgressLabel, Is.EqualTo("1100"));
        Assert.That(harness.Model.ProgressFraction, Is.EqualTo(0));
    }

    [Test]
    public async Task IncrementingAVolumeReader_MovesVolumesAndLeavesChaptersAlone()
    {
        var harness = new Harness();
        var entry = new MediaListEntry
        {
            Id = 1, MediaId = 53390, Progress = 0, ProgressVolumes = 20, Status = MediaListStatus.Current,
        };
        harness.ReturnsMedia(MangaMedia(chapters: 141, volumes: 34), entry);
        await harness.Model.LoadAsync(53390, entry);

        await harness.Model.IncrementProgressCommand.ExecuteAsync(null);

        Assert.That(entry.ProgressVolumes, Is.EqualTo(21));
        Assert.That(entry.Progress, Is.EqualTo(0));
    }

    private static Media MangaMedia(int? chapters, int? volumes) => new()
    {
        Id = 53390,
        Type = "MANGA",
        Format = "MANGA",
        Chapters = chapters,
        Volumes = volumes,
        Title = new MediaTitle { Romaji = "Shingeki no Kyojin" },
    };
}
