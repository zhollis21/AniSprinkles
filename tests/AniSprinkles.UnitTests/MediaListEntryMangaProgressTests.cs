namespace AniSprinkles.UnitTests;

/// <summary>
/// Tests the inferred progress unit for manga entries (#12). AniList keeps two independent
/// counters — <c>progress</c> (chapters) and <c>progressVolumes</c> — and never relates them or
/// the status; deciding which one drives the UI is entirely a client choice.
/// <para>
/// The rule mirrors AniHyou's: chapters by default, and volumes only for an entry that has volume
/// progress <em>and</em> no chapter progress. One counter is active at a time, and its total, its
/// completion check and its clamp all come from the same unit — so a reader who tracks by volume
/// gets a volume progress bar without anyone tracking chapters ever seeing one.
/// </para>
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class MediaListEntryMangaProgressTests
{
    public MediaListEntryMangaProgressTests() => TestDataBuilder.ResetAppSettings();

    private static MediaListEntry Manga(
        int? progress = null,
        int? progressVolumes = null,
        int? chapters = null,
        int? volumes = null,
        MediaListStatus? status = MediaListStatus.Current)
        => TestDataBuilder.Entry(
            1,
            status: status,
            progress: progress,
            progressVolumes: progressVolumes,
            mediaType: "MANGA",
            chapters: chapters,
            volumes: volumes);

    // ── Which unit is active ─────────────────────────────────────────

    [Theory]
    // progress, progressVolumes, expected volume mode
    [InlineData(null, null, false)]  // brand-new entry: chapters by default
    [InlineData(0, 0, false)]        // AniList returns 0/0 for a fresh entry, not null
    [InlineData(12, null, false)]    // chapter reader
    [InlineData(12, 3, false)]       // tracks both: chapters win
    [InlineData(0, 3, true)]         // volume-only reader
    [InlineData(null, 3, true)]      // volume-only reader, chapters never set
    public void UsesVolumeProgress_OnlyWhenVolumesSetAndChaptersAreNot(
        int? progress, int? progressVolumes, bool expected)
    {
        var entry = Manga(progress: progress, progressVolumes: progressVolumes, chapters: 141, volumes: 34);

        Assert.Equal(expected, entry.UsesVolumeProgress);
    }

    [Fact]
    public void UsesVolumeProgress_IsNeverTrueForAnime()
    {
        // The volumes field is meaningless for anime; a stray value must not flip the unit.
        var entry = TestDataBuilder.Entry(1, progress: 0, progressVolumes: 3, episodes: 12);

        Assert.False(entry.UsesVolumeProgress);
        Assert.Equal(MediaProgressUnit.Episode, entry.ActiveProgressUnit);
    }

    [Theory]
    [InlineData(12, null, MediaProgressUnit.Chapter)]
    [InlineData(0, 3, MediaProgressUnit.Volume)]
    public void ActiveProgressUnit_FollowsTheInferredMode(
        int? progress, int? progressVolumes, MediaProgressUnit expected)
    {
        var entry = Manga(progress: progress, progressVolumes: progressVolumes, chapters: 141, volumes: 34);

        Assert.Equal(expected, entry.ActiveProgressUnit);
    }

    // ── Active value, total and display ──────────────────────────────

    [Fact]
    public void ChapterMode_ReadsChaptersForValueAndTotal()
    {
        var entry = Manga(progress: 100, progressVolumes: null, chapters: 141, volumes: 34);

        Assert.Equal(100, entry.ActiveProgress);
        Assert.Equal(141, entry.ActiveProgressTotal);
        Assert.True(entry.HasKnownProgressTotal);
        Assert.Equal("100/141", entry.ProgressDisplay);
    }

    [Fact]
    public void VolumeMode_ReadsVolumesForValueAndTotal()
    {
        var entry = Manga(progress: 0, progressVolumes: 20, chapters: 141, volumes: 34);

        Assert.Equal(20, entry.ActiveProgress);
        Assert.Equal(34, entry.ActiveProgressTotal);
        Assert.True(entry.HasKnownProgressTotal);
        Assert.Equal("20/34", entry.ProgressDisplay);
    }

    [Fact]
    public void OngoingManga_HasNoTotalAtAll()
    {
        // The common case, and the one that most changes behaviour: AniList returns null chapters
        // AND null volumes for every RELEASING manga (verified across One Piece, Berserk, Dandadan,
        // Sakamoto Days). Unlike an airing anime there is no nextAiringEpisode to fall back on, so
        // there is no cap, no completion, and no progress bar.
        var entry = Manga(progress: 1100, chapters: null, volumes: null);

        Assert.Null(entry.ActiveProgressTotal);
        Assert.False(entry.HasKnownProgressTotal);
        Assert.Equal("1100", entry.ProgressDisplay);
        Assert.False(entry.IsCompletionAt(99999));
        Assert.Equal(99999, entry.ClampProgress(99999));
    }

    [Fact]
    public void VolumeMode_WithVolumeCountMissing_FallsBackToNoTotalRatherThanChapters()
    {
        // Some finished one-shots carry chapters but no volumes (verified: the Death Note one-shot
        // is chapters 1, volumes null). Borrowing the chapter total for a volume counter would put
        // "3/141" on screen, so the total is simply unknown in that case.
        var entry = Manga(progress: 0, progressVolumes: 3, chapters: 141, volumes: null);

        Assert.True(entry.UsesVolumeProgress);
        Assert.Null(entry.ActiveProgressTotal);
        Assert.False(entry.HasKnownProgressTotal);
    }

    // ── Completion and clamping follow the active unit ───────────────

    [Theory]
    [InlineData(33, false)]
    [InlineData(34, true)]
    [InlineData(35, true)]
    public void VolumeMode_CompletionIsCheckedAgainstTheVolumeTotal(int value, bool expected)
    {
        var entry = Manga(progress: 0, progressVolumes: 20, chapters: 141, volumes: 34);

        Assert.Equal(expected, entry.IsCompletionAt(value));
    }

    [Fact]
    public void ChapterMode_CompletionIsCheckedAgainstTheChapterTotal()
    {
        var entry = Manga(progress: 100, chapters: 141, volumes: 34);

        Assert.False(entry.IsCompletionAt(34)); // the volume total must not complete a chapter reader
        Assert.True(entry.IsCompletionAt(141));
    }

    [Fact]
    public void VolumeMode_ClampsToTheVolumeTotal()
    {
        var entry = Manga(progress: 0, progressVolumes: 20, chapters: 141, volumes: 34);

        Assert.Equal(34, entry.ClampProgress(99));
        Assert.Equal(0, entry.ClampProgress(-1));
    }

    // ── Writing back to the active unit ──────────────────────────────

    [Fact]
    public void SetActiveProgress_ChapterMode_WritesChaptersAndLeavesVolumesAlone()
    {
        var entry = Manga(progress: 100, progressVolumes: 12, chapters: 141, volumes: 34);

        entry.SetActiveProgress(101);

        Assert.Equal(101, entry.Progress);
        Assert.Equal(12, entry.ProgressVolumes);
    }

    [Fact]
    public void SetActiveProgress_VolumeMode_WritesVolumesAndLeavesChaptersAlone()
    {
        var entry = Manga(progress: 0, progressVolumes: 20, chapters: 141, volumes: 34);

        entry.SetActiveProgress(21);

        Assert.Equal(21, entry.ProgressVolumes);
        Assert.Equal(0, entry.Progress);
    }

    [Fact]
    public void SetActiveProgress_VolumeModeDecrementedToZero_FallsBackToChapters()
    {
        // Pinning a real edge of the inferred rule rather than pretending it doesn't exist: the
        // mode is derived, so emptying the volume counter drops the entry back to chapter mode.
        // Both counters read 0 at that point, so the only visible change is the unit word.
        var entry = Manga(progress: 0, progressVolumes: 1, chapters: 141, volumes: 34);

        entry.SetActiveProgress(0);

        Assert.Equal(0, entry.ProgressVolumes);
        Assert.False(entry.UsesVolumeProgress);
        Assert.Equal(MediaProgressUnit.Chapter, entry.ActiveProgressUnit);
    }

    // ── Airing members stay inert for manga ──────────────────────────

    [Fact]
    public void Manga_HasNoAiringInfo()
    {
        // These already key off NextAiringEpisode, which AniList never populates for manga, so they
        // need no type check. Asserted so a future change to them can't quietly start claiming a
        // manga is "3 chapters behind".
        var entry = Manga(progress: 100, chapters: 141);

        Assert.Null(entry.EpisodesBehind);
        Assert.Null(entry.NextEpisodeDisplay);
        Assert.Null(entry.AiringInfoDisplay);
        Assert.False(entry.HasAiringInfo);
    }

    // ── The anime path is unchanged ──────────────────────────────────

    [Fact]
    public void Anime_StillUsesEpisodesAndTheAiringFallback()
    {
        var finite = TestDataBuilder.Entry(1, progress: 5, episodes: 12);
        Assert.Equal(MediaProgressUnit.Episode, finite.ActiveProgressUnit);
        Assert.Equal(12, finite.ActiveProgressTotal);
        Assert.True(finite.HasKnownProgressTotal);

        // Long-running airing show: a soft cap from nextAiringEpisode, but no finite total.
        var airing = TestDataBuilder.Entry(2, progress: 1000, episodes: null, nextAiringEpisode: 1088);
        Assert.Equal(1087, airing.ActiveProgressTotal);
        Assert.False(airing.HasKnownProgressTotal);
        Assert.False(airing.IsCompletionAt(1087));
    }
}
