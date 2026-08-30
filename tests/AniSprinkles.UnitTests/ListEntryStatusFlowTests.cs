using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The completion flow's user-visible copy and its write-back (#12). The prompt is the one place a
/// reader is told what they just finished, so saying "episodes watched" over a manga — or filling
/// the chapter counter for someone tracking volumes — is a visible wrong answer rather than an
/// internal detail.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class ListEntryStatusFlowTests
{
    public ListEntryStatusFlowTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task Completion_ForAnime_TalksAboutEpisodesWatched()
    {
        var dialogs = new ScriptedDialogService { ConfirmAnswer = true };
        var entry = TestDataBuilder.Entry(1, progress: 12, episodes: 12);

        Assert.True(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry));

        Assert.Equal("All episodes watched!", dialogs.LastConfirmTitle);
        Assert.Contains("watched all 12 episodes", dialogs.LastConfirmMessage);
        Assert.Equal(12, entry.Progress);
    }

    [Fact]
    public async Task Completion_ForAMangaChapterReader_TalksAboutChaptersRead()
    {
        var dialogs = new ScriptedDialogService { ConfirmAnswer = true };
        var entry = TestDataBuilder.Entry(
            1, progress: 141, mediaType: "MANGA", chapters: 141, volumes: 34);

        Assert.True(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry));

        Assert.Equal("All chapters read!", dialogs.LastConfirmTitle);
        Assert.Contains("read all 141 chapters", dialogs.LastConfirmMessage);
        Assert.Equal(141, entry.Progress);
        Assert.Equal(MediaListStatus.Completed, entry.Status);
    }

    [Fact]
    public async Task Completion_ForAMangaVolumeReader_TalksAboutVolumesAndFillsTheVolumeCounter()
    {
        var dialogs = new ScriptedDialogService { ConfirmAnswer = true };
        var entry = TestDataBuilder.Entry(
            1, progress: 0, progressVolumes: 34, mediaType: "MANGA", chapters: 141, volumes: 34);

        Assert.True(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry));

        Assert.Equal("All volumes read!", dialogs.LastConfirmTitle);
        Assert.Contains("read all 34 volumes", dialogs.LastConfirmMessage);
        Assert.Equal(34, entry.ProgressVolumes);
        // The chapter counter is a separate field on AniList and must come back untouched.
        Assert.Equal(0, entry.Progress);
    }

    [Fact]
    public async Task Completion_ForAnOngoingManga_NeverRuns()
    {
        // AniList returns null chapters and null volumes for every RELEASING series, so there is no
        // total to have reached. Without a cap the caller should never get here, and if it does the
        // flow must decline rather than complete a series at an arbitrary count.
        var dialogs = new ScriptedDialogService { ConfirmAnswer = true };
        var entry = TestDataBuilder.Entry(1, progress: 1100, mediaType: "MANGA");

        Assert.False(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry));

        Assert.DoesNotContain(nameof(IDialogService.ConfirmAsync), dialogs.Calls);
        Assert.Equal(1100, entry.Progress);
        Assert.NotEqual(MediaListStatus.Completed, entry.Status);
    }

    [Fact]
    public async Task Completion_WhenDeclined_ChangesNothing()
    {
        var dialogs = new ScriptedDialogService { ConfirmAnswer = false };
        var entry = TestDataBuilder.Entry(
            1, progress: 100, mediaType: "MANGA", chapters: 141, volumes: 34);

        Assert.False(await new ListEntryStatusFlow(dialogs).ApplyCompletionAsync(entry));

        Assert.Equal(100, entry.Progress);
        Assert.NotEqual(MediaListStatus.Completed, entry.Status);
    }
}
