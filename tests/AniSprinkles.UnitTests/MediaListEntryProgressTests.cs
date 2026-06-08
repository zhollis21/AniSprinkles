namespace AniSprinkles.UnitTests;

/// <summary>
/// Tests the shared progress helpers on <see cref="MediaListEntry"/> that the Details page and My
/// Anime (+1 and Edit-progress) both route through: <see cref="MediaListEntry.IsCompletionAt"/> and
/// <see cref="MediaListEntry.ClampProgress"/>.
/// </summary>
public class MediaListEntryProgressTests
{
    public MediaListEntryProgressTests() => TestDataBuilder.ResetAppSettings();

    // ── IsCompletionAt ───────────────────────────────────────────────

    [Theory]
    [InlineData(11, false)] // below the total
    [InlineData(12, true)]  // exactly the total
    [InlineData(13, true)]  // past the total
    public void IsCompletionAt_WithKnownTotal_TrueAtOrAboveMax(int progress, bool expected)
    {
        var entry = TestDataBuilder.Entry(1, status: MediaListStatus.Current, episodes: 12);

        Assert.Equal(expected, entry.IsCompletionAt(progress));
    }

    [Fact]
    public void IsCompletionAt_AlreadyCompleted_IsFalse()
    {
        var entry = TestDataBuilder.Entry(1, status: MediaListStatus.Completed, episodes: 12);

        Assert.False(entry.IsCompletionAt(12));
    }

    [Fact]
    public void IsCompletionAt_WithoutKnownTotal_IsFalse()
    {
        // Long-running airing show: a next-airing episode gives MaxEpisodes a value, but completion
        // requires a finite total (HasKnownEpisodeCount), so reaching the latest aired episode is
        // never a completion.
        var entry = TestDataBuilder.Entry(
            1, status: MediaListStatus.Current, episodes: null, nextAiringEpisode: 1088);

        Assert.False(entry.IsCompletionAt(1087));
        Assert.False(entry.IsCompletionAt(99999));
    }

    // ── ClampProgress ────────────────────────────────────────────────

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(12, 12)]
    [InlineData(99, 12)] // clamped to the known total
    public void ClampProgress_WithKnownMax_ClampsToZeroAndMax(int value, int expected)
    {
        var entry = TestDataBuilder.Entry(1, status: MediaListStatus.Current, episodes: 12);

        Assert.Equal(expected, entry.ClampProgress(value));
    }

    [Fact]
    public void ClampProgress_AiringShow_ClampsToLatestAiredEpisode()
    {
        // No finite total, but a next-airing episode caps progress at the latest aired episode
        // (MaxEpisodes = NextAiringEpisode - 1), matching the Details page slider bound.
        var entry = TestDataBuilder.Entry(
            1, status: MediaListStatus.Current, episodes: null, nextAiringEpisode: 1088);

        Assert.Equal(1087, entry.ClampProgress(99999));
        Assert.Equal(0, entry.ClampProgress(-1));
    }

    [Fact]
    public void ClampProgress_NoMaxAtAll_OnlyClampsLowerBound()
    {
        // No total and no next-airing episode: no upper bound, only the floor of 0.
        var entry = TestDataBuilder.Entry(1, status: MediaListStatus.Current, episodes: null);

        Assert.Equal(5000, entry.ClampProgress(5000));
        Assert.Equal(0, entry.ClampProgress(-3));
    }
}
