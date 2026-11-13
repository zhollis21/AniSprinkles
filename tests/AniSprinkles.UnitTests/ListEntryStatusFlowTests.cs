using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The completion flow's user-visible copy and its write-back (#12). The prompt is the one place a
/// reader is told what they just finished, so saying "episodes watched" over a manga — or filling
/// the chapter counter for someone tracking volumes — is a visible wrong answer rather than an
/// internal detail.
/// </summary>
[NonParallelizable]
public class ListEntryStatusFlowTests
{
    public ListEntryStatusFlowTests() => TestDataBuilder.ResetAppSettings();

    [Test]
    public async Task Completion_ForAnime_TalksAboutEpisodesWatched()
    {
        var dialogs = new ScriptedDialogService { ConfirmAnswer = true };
        var entry = TestDataBuilder.Entry(1, progress: 12, episodes: 12);

        Assert.That(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry), Is.True);

        Assert.That(dialogs.LastConfirmTitle, Is.EqualTo("All episodes watched!"));
        Assert.That(dialogs.LastConfirmMessage, Does.Contain("watched all 12 episodes"));
        Assert.That(entry.Progress, Is.EqualTo(12));
    }

    [Test]
    public async Task Completion_ForAMangaChapterReader_TalksAboutChaptersRead()
    {
        var dialogs = new ScriptedDialogService { ConfirmAnswer = true };
        var entry = TestDataBuilder.Entry(
            1, progress: 141, mediaType: "MANGA", chapters: 141, volumes: 34);

        Assert.That(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry), Is.True);

        Assert.That(dialogs.LastConfirmTitle, Is.EqualTo("All chapters read!"));
        Assert.That(dialogs.LastConfirmMessage, Does.Contain("read all 141 chapters"));
        Assert.That(entry.Progress, Is.EqualTo(141));
        Assert.That(entry.Status, Is.EqualTo(MediaListStatus.Completed));
    }

    [Test]
    public async Task Completion_ForAMangaVolumeReader_TalksAboutVolumesAndFillsTheVolumeCounter()
    {
        var dialogs = new ScriptedDialogService { ConfirmAnswer = true };
        var entry = TestDataBuilder.Entry(
            1, progress: 0, progressVolumes: 34, mediaType: "MANGA", chapters: 141, volumes: 34);

        Assert.That(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry), Is.True);

        Assert.That(dialogs.LastConfirmTitle, Is.EqualTo("All volumes read!"));
        Assert.That(dialogs.LastConfirmMessage, Does.Contain("read all 34 volumes"));
        Assert.That(entry.ProgressVolumes, Is.EqualTo(34));
        // The chapter counter is a separate field on AniList and must come back untouched.
        Assert.That(entry.Progress, Is.EqualTo(0));
    }

    [Test]
    public async Task Completion_ForAnOngoingManga_NeverRuns()
    {
        // AniList returns null chapters and null volumes for every RELEASING series, so there is no
        // total to have reached. Without a cap the caller should never get here, and if it does the
        // flow must decline rather than complete a series at an arbitrary count.
        var dialogs = new ScriptedDialogService { ConfirmAnswer = true };
        var entry = TestDataBuilder.Entry(1, progress: 1100, mediaType: "MANGA");

        Assert.That(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry), Is.False);

        Assert.That(dialogs.Calls, Does.Not.Contain(nameof(IDialogService.ConfirmAsync)));
        Assert.That(entry.Progress, Is.EqualTo(1100));
        Assert.That(entry.Status, Is.Not.EqualTo(MediaListStatus.Completed));
    }

    [Test]
    public async Task Completion_WhenDeclined_ChangesNothing()
    {
        var dialogs = new ScriptedDialogService { ConfirmAnswer = false };
        var entry = TestDataBuilder.Entry(
            1, progress: 100, mediaType: "MANGA", chapters: 141, volumes: 34);

        Assert.That(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry), Is.False);

        Assert.That(entry.Progress, Is.EqualTo(100));
        Assert.That(entry.Status, Is.Not.EqualTo(MediaListStatus.Completed));
    }
}
