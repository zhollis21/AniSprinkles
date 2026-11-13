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
[NonParallelizable]
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

    [TestCase(null, null, false)]  // brand-new entry: chapters by default
    [TestCase(0, 0, false)]        // AniList returns 0/0 for a fresh entry, not null
    [TestCase(12, null, false)]    // chapter reader
    [TestCase(12, 3, false)]       // tracks both: chapters win
    [TestCase(0, 3, true)]         // volume-only reader
    [TestCase(null, 3, true)]      // volume-only reader, chapters never set
    public void UsesVolumeProgress_OnlyWhenVolumesSetAndChaptersAreNot(
        int? progress, int? progressVolumes, bool expected)
    {
        var entry = Manga(progress: progress, progressVolumes: progressVolumes, chapters: 141, volumes: 34);

        Assert.That(entry.UsesVolumeProgress, Is.EqualTo(expected));
    }

    [Test]
    public void UsesVolumeProgress_IsNeverTrueForAnime()
    {
        // The volumes field is meaningless for anime; a stray value must not flip the unit.
        var entry = TestDataBuilder.Entry(1, progress: 0, progressVolumes: 3, episodes: 12);

        Assert.That(entry.UsesVolumeProgress, Is.False);
        Assert.That(entry.ActiveProgressUnit, Is.EqualTo(MediaProgressUnit.Episode));
    }

    [TestCase(12, null, MediaProgressUnit.Chapter)]
    [TestCase(0, 3, MediaProgressUnit.Volume)]
    public void ActiveProgressUnit_FollowsTheInferredMode(
        int? progress, int? progressVolumes, MediaProgressUnit expected)
    {
        var entry = Manga(progress: progress, progressVolumes: progressVolumes, chapters: 141, volumes: 34);

        Assert.That(entry.ActiveProgressUnit, Is.EqualTo(expected));
    }

    // ── Active value, total and display ──────────────────────────────

    [Test]
    public void ChapterMode_ReadsChaptersForValueAndTotal()
    {
        var entry = Manga(progress: 100, progressVolumes: null, chapters: 141, volumes: 34);

        Assert.That(entry.ActiveProgress, Is.EqualTo(100));
        Assert.That(entry.ActiveProgressTotal, Is.EqualTo(141));
        Assert.That(entry.HasKnownProgressTotal, Is.True);
        Assert.That(entry.ProgressDisplay, Is.EqualTo("100/141"));
    }

    [Test]
    public void VolumeMode_ReadsVolumesForValueAndTotal()
    {
        var entry = Manga(progress: 0, progressVolumes: 20, chapters: 141, volumes: 34);

        Assert.That(entry.ActiveProgress, Is.EqualTo(20));
        Assert.That(entry.ActiveProgressTotal, Is.EqualTo(34));
        Assert.That(entry.HasKnownProgressTotal, Is.True);
        Assert.That(entry.ProgressDisplay, Is.EqualTo("20/34"));
    }

    [Test]
    public void OngoingManga_HasNoTotalAtAll()
    {
        // The common case, and the one that most changes behaviour: AniList returns null chapters
        // AND null volumes for every RELEASING manga (verified across One Piece, Berserk, Dandadan,
        // Sakamoto Days). Unlike an airing anime there is no nextAiringEpisode to fall back on, so
        // there is no cap, no completion, and no progress bar.
        var entry = Manga(progress: 1100, chapters: null, volumes: null);

        Assert.That(entry.ActiveProgressTotal, Is.Null);
        Assert.That(entry.HasKnownProgressTotal, Is.False);
        Assert.That(entry.ProgressDisplay, Is.EqualTo("1100"));
        Assert.That(entry.IsCompletionAt(99999), Is.False);
        Assert.That(entry.ClampProgress(99999), Is.EqualTo(99999));
    }

    [Test]
    public void VolumeMode_WithVolumeCountMissing_FallsBackToNoTotalRatherThanChapters()
    {
        // Some finished one-shots carry chapters but no volumes (verified: the Death Note one-shot
        // is chapters 1, volumes null). Borrowing the chapter total for a volume counter would put
        // "3/141" on screen, so the total is simply unknown in that case.
        var entry = Manga(progress: 0, progressVolumes: 3, chapters: 141, volumes: null);

        Assert.That(entry.UsesVolumeProgress, Is.True);
        Assert.That(entry.ActiveProgressTotal, Is.Null);
        Assert.That(entry.HasKnownProgressTotal, Is.False);
    }

    // ── Completion and clamping follow the active unit ───────────────

    [TestCase(33, false)]
    [TestCase(34, true)]
    [TestCase(35, true)]
    public void VolumeMode_CompletionIsCheckedAgainstTheVolumeTotal(int value, bool expected)
    {
        var entry = Manga(progress: 0, progressVolumes: 20, chapters: 141, volumes: 34);

        Assert.That(entry.IsCompletionAt(value), Is.EqualTo(expected));
    }

    [Test]
    public void ChapterMode_CompletionIsCheckedAgainstTheChapterTotal()
    {
        var entry = Manga(progress: 100, chapters: 141, volumes: 34);

        Assert.That(entry.IsCompletionAt(34), Is.False); // the volume total must not complete a chapter reader
        Assert.That(entry.IsCompletionAt(141), Is.True);
    }

    [Test]
    public void VolumeMode_ClampsToTheVolumeTotal()
    {
        var entry = Manga(progress: 0, progressVolumes: 20, chapters: 141, volumes: 34);

        Assert.That(entry.ClampProgress(99), Is.EqualTo(34));
        Assert.That(entry.ClampProgress(-1), Is.EqualTo(0));
    }

    // ── Writing back to the active unit ──────────────────────────────

    [Test]
    public void SetActiveProgress_ChapterMode_WritesChaptersAndLeavesVolumesAlone()
    {
        var entry = Manga(progress: 100, progressVolumes: 12, chapters: 141, volumes: 34);

        entry.SetActiveProgress(101);

        Assert.That(entry.Progress, Is.EqualTo(101));
        Assert.That(entry.ProgressVolumes, Is.EqualTo(12));
    }

    [Test]
    public void SetActiveProgress_VolumeMode_WritesVolumesAndLeavesChaptersAlone()
    {
        var entry = Manga(progress: 0, progressVolumes: 20, chapters: 141, volumes: 34);

        entry.SetActiveProgress(21);

        Assert.That(entry.ProgressVolumes, Is.EqualTo(21));
        Assert.That(entry.Progress, Is.EqualTo(0));
    }

    [Test]
    public void SetActiveProgress_VolumeModeDecrementedToZero_FallsBackToChapters()
    {
        // Pinning a real edge of the inferred rule rather than pretending it doesn't exist: the
        // mode is derived, so emptying the volume counter drops the entry back to chapter mode.
        // Both counters read 0 at that point, so the only visible change is the unit word.
        var entry = Manga(progress: 0, progressVolumes: 1, chapters: 141, volumes: 34);

        entry.SetActiveProgress(0);

        Assert.That(entry.ProgressVolumes, Is.EqualTo(0));
        Assert.That(entry.UsesVolumeProgress, Is.False);
        Assert.That(entry.ActiveProgressUnit, Is.EqualTo(MediaProgressUnit.Chapter));
    }

    // ── Airing members stay inert for manga ──────────────────────────

    [Test]
    public void Manga_HasNoAiringInfo()
    {
        // These already key off NextAiringEpisode, which AniList never populates for manga, so they
        // need no type check. Asserted so a future change to them can't quietly start claiming a
        // manga is "3 chapters behind".
        var entry = Manga(progress: 100, chapters: 141);

        Assert.That(entry.EpisodesBehind, Is.Null);
        Assert.That(entry.NextEpisodeDisplay, Is.Null);
        Assert.That(entry.AiringInfoDisplay, Is.Null);
        Assert.That(entry.HasAiringInfo, Is.False);
    }

    // ── The anime path is unchanged ──────────────────────────────────

    [Test]
    public void Anime_StillUsesEpisodesAndTheAiringFallback()
    {
        var finite = TestDataBuilder.Entry(1, progress: 5, episodes: 12);
        Assert.That(finite.ActiveProgressUnit, Is.EqualTo(MediaProgressUnit.Episode));
        Assert.That(finite.ActiveProgressTotal, Is.EqualTo(12));
        Assert.That(finite.HasKnownProgressTotal, Is.True);

        // Long-running airing show: a soft cap from nextAiringEpisode, but no finite total.
        var airing = TestDataBuilder.Entry(2, progress: 1000, episodes: null, nextAiringEpisode: 1088);
        Assert.That(airing.ActiveProgressTotal, Is.EqualTo(1087));
        Assert.That(airing.HasKnownProgressTotal, Is.False);
        Assert.That(airing.IsCompletionAt(1087), Is.False);
    }

    // ── The +1 control's labels ──────────────────────────────────────

    [Test]
    public void IncrementLabel_UsesTheActiveUnitsAbbreviation()
    {
        Assert.That(TestDataBuilder.Entry(1, progress: 5, episodes: 12).IncrementLabel, Is.EqualTo("+1 EP"));
        Assert.That(Manga(progress: 100, chapters: 141).IncrementLabel, Is.EqualTo("+1 CH"));
        Assert.That(Manga(progressVolumes: 20).IncrementLabel, Is.EqualTo("+1 VOL"));
    }

    [Test]
    public void IncrementDescription_TracksTheActiveUnitRatherThanSayingEpisode()
    {
        // The screen-reader text sat hardcoded as "Increment episode progress" in
        // MediaListLoadedContentView while the visible label beside it was already bound to
        // IncrementLabel. Harmless while the view was anime-only; once #12 shared it with manga,
        // TalkBack announced "episode" over a +1 VOL button.
        Assert.That(
            TestDataBuilder.Entry(1, progress: 5, episodes: 12).IncrementDescription,
            Is.EqualTo("Increment episode progress"));
        Assert.That(
            Manga(progress: 100, chapters: 141).IncrementDescription,
            Is.EqualTo("Increment chapter progress"));
        Assert.That(
            Manga(progressVolumes: 20).IncrementDescription,
            Is.EqualTo("Increment volume progress"));
    }

    [Test]
    public void IncrementHint_UsesTheUnitsOwnVerbAndNoun()
    {
        // The hint sat hardcoded beside the description with the same anime-only wording. The
        // anime string is unchanged on purpose — only the manga cases are new.
        Assert.That(
            TestDataBuilder.Entry(1, progress: 5, episodes: 12).IncrementHint,
            Is.EqualTo("Adds one to the watched episode count"));
        Assert.That(
            Manga(progress: 100, chapters: 141).IncrementHint,
            Is.EqualTo("Adds one to the read chapter count"));
        Assert.That(
            Manga(progressVolumes: 20).IncrementHint,
            Is.EqualTo("Adds one to the read volume count"));
    }

    [Test]
    public void IncrementLabels_RaiseChangeNotificationsWhenEitherCounterMoves()
    {
        // Both labels are computed, so the +1 button keeps stale text unless each counter
        // notifies them — and switching a manga between chapter and volume mode is exactly a
        // write to one of these two fields.
        var entry = Manga(progressVolumes: 3);
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.Progress = 7;
        entry.ProgressVolumes = 4;

        Assert.That(changed.Count(n => n == nameof(MediaListEntry.IncrementLabel)), Is.EqualTo(2));
        Assert.That(changed.Count(n => n == nameof(MediaListEntry.IncrementDescription)), Is.EqualTo(2));
    }
}
