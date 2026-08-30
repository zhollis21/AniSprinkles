namespace AniSprinkles.UnitTests;

/// <summary>
/// Tests <see cref="ListEntryStatusMutations"/>, the pure mutation half of the
/// status-change flow. The UI half (<c>ListEntryStatusFlow</c>) depends on MAUI
/// popups and is covered by manual testing.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class ListEntryStatusMutationsTests
{
    public ListEntryStatusMutationsTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public void ApplyStatusChange_Repeating_ResetsProgress_And_IncrementsRepeat()
    {
        var entry = TestDataBuilder.Entry(1, progress: 12, status: MediaListStatus.Completed, episodes: 12);
        entry.Repeat = 3;

        var needsScorePrompt = ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Repeating);

        Assert.False(needsScorePrompt);
        Assert.Equal(MediaListStatus.Repeating, entry.Status);
        Assert.Equal(0, entry.Progress);
        Assert.Equal(4, entry.Repeat);
    }

    [Fact]
    public void ApplyStatusChange_Repeating_FromNullRepeat_StartsAtOne()
    {
        var entry = TestDataBuilder.Entry(1, progress: 3, status: MediaListStatus.Current);
        // Repeat defaults to null.

        ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Repeating);

        Assert.Equal(1, entry.Repeat);
    }

    [Fact]
    public void ApplyStatusChange_Paused_DoesNotChangeProgressOrRepeat()
    {
        var entry = TestDataBuilder.Entry(1, progress: 5, status: MediaListStatus.Current, episodes: 12);
        entry.Repeat = 2;

        var needsScorePrompt = ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Paused);

        Assert.False(needsScorePrompt);
        Assert.Equal(MediaListStatus.Paused, entry.Status);
        Assert.Equal(5, entry.Progress);
        Assert.Equal(2, entry.Repeat);
    }

    [Fact]
    public void ApplyStatusChange_Completed_WithKnownTotal_FillsProgress_AndRequestsScorePrompt()
    {
        var entry = TestDataBuilder.Entry(1, progress: 8, status: MediaListStatus.Current, episodes: 12);

        var needsScorePrompt = ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Completed);

        Assert.True(needsScorePrompt);
        Assert.Equal(MediaListStatus.Completed, entry.Status);
        Assert.Equal(12, entry.Progress);
    }

    [Fact]
    public void ApplyStatusChange_Completed_WithoutKnownTotal_LeavesProgress_ButStillRequestsScorePrompt()
    {
        // Long-running airing show: no total episode count, only a next-airing episode.
        var entry = TestDataBuilder.Entry(
            1, progress: 1000, status: MediaListStatus.Current,
            episodes: null, nextAiringEpisode: 1088);

        var needsScorePrompt = ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Completed);

        Assert.True(needsScorePrompt);
        Assert.Equal(MediaListStatus.Completed, entry.Status);
        Assert.Equal(1000, entry.Progress);
    }

    [Fact]
    public void ApplyStatusChange_Current_FromCompletedAtCap_WalksProgressBackByOne()
    {
        // Completed → Watching with progress already at max would leave the +1 button
        // dead. Walk progress back by one so there's an episode to watch.
        var entry = TestDataBuilder.Entry(1, progress: 12, status: MediaListStatus.Completed, episodes: 12);

        var needsScorePrompt = ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Current);

        Assert.False(needsScorePrompt);
        Assert.Equal(MediaListStatus.Current, entry.Status);
        Assert.Equal(11, entry.Progress);
    }

    [Fact]
    public void ApplyStatusChange_Current_BelowCap_LeavesProgressUntouched()
    {
        var entry = TestDataBuilder.Entry(1, progress: 5, status: MediaListStatus.Paused, episodes: 12);

        ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Current);

        Assert.Equal(MediaListStatus.Current, entry.Status);
        Assert.Equal(5, entry.Progress);
    }

    [Fact]
    public void ApplyStatusChange_Current_AiringShowAtAiredCap_LeavesProgressUntouched()
    {
        // No known total, only a next-airing episode. User is caught up — that's
        // a normal Watching state, not the dead-button scenario.
        var entry = TestDataBuilder.Entry(
            1, progress: 1087, status: MediaListStatus.Completed,
            episodes: null, nextAiringEpisode: 1088);

        ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Current);

        Assert.Equal(MediaListStatus.Current, entry.Status);
        Assert.Equal(1087, entry.Progress);
    }

    // ── Manga (#12) ──────────────────────────────────────────

    [Fact]
    public void ApplyStatusChange_Completed_MangaChapterReader_FillsChaptersNotEpisodes()
    {
        var entry = TestDataBuilder.Entry(
            1, progress: 100, status: MediaListStatus.Current,
            mediaType: "MANGA", chapters: 141, volumes: 34);

        ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Completed);

        Assert.Equal(141, entry.Progress);
        Assert.Null(entry.ProgressVolumes);
    }

    [Fact]
    public void ApplyStatusChange_Completed_MangaVolumeReader_FillsVolumesNotChapters()
    {
        var entry = TestDataBuilder.Entry(
            1, progress: 0, progressVolumes: 20, status: MediaListStatus.Current,
            mediaType: "MANGA", chapters: 141, volumes: 34);

        ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Completed);

        Assert.Equal(34, entry.ProgressVolumes);
        Assert.Equal(0, entry.Progress);
    }

    [Fact]
    public void ApplyStatusChange_Completed_OngoingManga_LeavesProgressAloneForWantOfATotal()
    {
        // AniList returns null chapters AND null volumes for every RELEASING series, and manga has
        // no nextAiringEpisode to fall back on, so there is nothing to fill progress up to.
        var entry = TestDataBuilder.Entry(
            1, progress: 1100, status: MediaListStatus.Current, mediaType: "MANGA");

        ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Completed);

        Assert.Equal(MediaListStatus.Completed, entry.Status);
        Assert.Equal(1100, entry.Progress);
    }

    [Fact]
    public void ApplyStatusChange_Repeating_ClearsBothCounters()
    {
        // Clearing only chapters would leave volumes above zero with chapters at zero — exactly the
        // shape UsesVolumeProgress reads as “this reader tracks volumes”, so a freshly reset entry
        // would flip to volume mode and show a full 24/24 volume count on a reread that just began.
        var entry = TestDataBuilder.Entry(
            1, progress: 232, progressVolumes: 24, status: MediaListStatus.Completed,
            mediaType: "MANGA", chapters: 232, volumes: 24);

        ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Repeating);

        Assert.Equal(0, entry.Progress);
        Assert.Equal(0, entry.ProgressVolumes);
        Assert.False(entry.UsesVolumeProgress);
        Assert.Equal(1, entry.Repeat);
    }

    [Fact]
    public void ApplyStatusChange_Current_MangaVolumeReaderAtCap_WalksTheVolumeCountBack()
    {
        var entry = TestDataBuilder.Entry(
            1, progress: 0, progressVolumes: 34, status: MediaListStatus.Completed,
            mediaType: "MANGA", chapters: 141, volumes: 34);

        ListEntryStatusMutations.ApplyStatusChange(entry, MediaListStatus.Current);

        Assert.Equal(33, entry.ProgressVolumes);
        Assert.Equal(0, entry.Progress);
    }
}
