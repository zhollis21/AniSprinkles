namespace AniSprinkles.UnitTests;

/// <summary>
/// Tests the computed predicates that drive which rows the long-press action menu shows:
/// <see cref="MediaListEntry.CanEditProgress"/> and <see cref="MediaListEntry.CanMarkCompleted"/>.
/// The popup/PageModel routing itself depends on MAUI popups and is covered by manual testing.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class MediaListEntryActionPredicatesTests
{
    public MediaListEntryActionPredicatesTests() => TestDataBuilder.ResetAppSettings();

    [Theory]
    [InlineData(MediaListStatus.Current, true)]
    [InlineData(MediaListStatus.Repeating, true)]
    [InlineData(MediaListStatus.Paused, true)]
    [InlineData(MediaListStatus.Dropped, true)]
    [InlineData(MediaListStatus.Planning, false)]
    [InlineData(MediaListStatus.Completed, false)]
    public void CanEditProgress_HiddenForPlanningAndCompleted_ShownOtherwise(MediaListStatus status, bool expected)
    {
        var entry = TestDataBuilder.Entry(1, status: status, episodes: 12);

        Assert.Equal(expected, entry.CanEditProgress);
    }

    [Theory]
    // Allowed from any non-completed list when a finite total is known — users forget to move a
    // finished show out of Planning/Paused/etc.
    [InlineData(MediaListStatus.Current, true)]
    [InlineData(MediaListStatus.Repeating, true)]
    [InlineData(MediaListStatus.Planning, true)]
    [InlineData(MediaListStatus.Paused, true)]
    [InlineData(MediaListStatus.Dropped, true)]
    [InlineData(MediaListStatus.Completed, false)]
    public void CanMarkCompleted_WithKnownTotal_AllowedFromAnyListExceptCompleted(MediaListStatus status, bool expected)
    {
        var entry = TestDataBuilder.Entry(1, status: status, episodes: 12);

        Assert.Equal(expected, entry.CanMarkCompleted);
    }

    [Fact]
    public void CanMarkCompleted_WithoutKnownTotal_IsFalse()
    {
        // Long-running airing show: no finite total, only a next-airing episode. Completion can't
        // be applied without a known total, so the action is hidden.
        var entry = TestDataBuilder.Entry(
            1, status: MediaListStatus.Current, episodes: null, nextAiringEpisode: 1088);

        Assert.False(entry.CanMarkCompleted);
    }
}
