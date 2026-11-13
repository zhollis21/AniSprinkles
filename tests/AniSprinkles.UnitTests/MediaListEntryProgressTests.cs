namespace AniSprinkles.UnitTests;

/// <summary>
/// Tests the shared progress helpers on <see cref="MediaListEntry"/> that the Details page and My
/// Anime (+1 and Edit-progress) both route through: <see cref="MediaListEntry.IsCompletionAt"/> and
/// <see cref="MediaListEntry.ClampProgress"/>.
/// </summary>
[NonParallelizable]
public class MediaListEntryProgressTests
{
    public MediaListEntryProgressTests() => TestDataBuilder.ResetAppSettings();

    // ── IsCompletionAt ───────────────────────────────────────────────

    [TestCase(11, false)] // below the total
    [TestCase(12, true)]  // exactly the total
    [TestCase(13, true)]  // past the total
    public void IsCompletionAt_WithKnownTotal_TrueAtOrAboveMax(int progress, bool expected)
    {
        var entry = TestDataBuilder.Entry(1, status: MediaListStatus.Current, episodes: 12);

        Assert.That(entry.IsCompletionAt(progress), Is.EqualTo(expected));
    }

    [Test]
    public void IsCompletionAt_AlreadyCompleted_IsFalse()
    {
        var entry = TestDataBuilder.Entry(1, status: MediaListStatus.Completed, episodes: 12);

        Assert.That(entry.IsCompletionAt(12), Is.False);
    }

    [Test]
    public void IsCompletionAt_WithoutKnownTotal_IsFalse()
    {
        // Long-running airing show: a next-airing episode gives ActiveProgressTotal a value, but
        // completion requires a finite total (HasKnownProgressTotal), so reaching the latest aired episode is
        // never a completion.
        var entry = TestDataBuilder.Entry(
            1, status: MediaListStatus.Current, episodes: null, nextAiringEpisode: 1088);

        Assert.That(entry.IsCompletionAt(1087), Is.False);
        Assert.That(entry.IsCompletionAt(99999), Is.False);
    }

    // ── ClampProgress ────────────────────────────────────────────────

    [TestCase(-5, 0)]
    [TestCase(0, 0)]
    [TestCase(7, 7)]
    [TestCase(12, 12)]
    [TestCase(99, 12)] // clamped to the known total
    public void ClampProgress_WithKnownMax_ClampsToZeroAndMax(int value, int expected)
    {
        var entry = TestDataBuilder.Entry(1, status: MediaListStatus.Current, episodes: 12);

        Assert.That(entry.ClampProgress(value), Is.EqualTo(expected));
    }

    [Test]
    public void ClampProgress_AiringShow_ClampsToLatestAiredEpisode()
    {
        // No finite total, but a next-airing episode caps progress at the latest aired episode
        // (ActiveProgressTotal = NextAiringEpisode - 1), matching the Details page slider bound.
        var entry = TestDataBuilder.Entry(
            1, status: MediaListStatus.Current, episodes: null, nextAiringEpisode: 1088);

        Assert.That(entry.ClampProgress(99999), Is.EqualTo(1087));
        Assert.That(entry.ClampProgress(-1), Is.EqualTo(0));
    }

    [Test]
    public void ClampProgress_NoMaxAtAll_OnlyClampsLowerBound()
    {
        // No total and no next-airing episode: no upper bound, only the floor of 0.
        var entry = TestDataBuilder.Entry(1, status: MediaListStatus.Current, episodes: null);

        Assert.That(entry.ClampProgress(5000), Is.EqualTo(5000));
        Assert.That(entry.ClampProgress(-3), Is.EqualTo(0));
    }
}
