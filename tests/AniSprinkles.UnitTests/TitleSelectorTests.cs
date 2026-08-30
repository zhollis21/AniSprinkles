namespace AniSprinkles.UnitTests;

/// <summary>
/// #141. The title-language fallback chain existed in three places — <c>Media.DisplayTitle</c>,
/// <c>RelatedMedia.DisplayTitle</c>, and the airing worker's own copy, whose doc comment claimed it
/// matched the app UI with nothing checking that. These pin the one shared chain, and that the two
/// model properties now route through it.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class TitleSelectorTests
{
    [Theory]
    [InlineData(UserTitleLanguage.Romaji, "Shingeki no Kyojin")]
    [InlineData(UserTitleLanguage.English, "Attack on Titan")]
    [InlineData(UserTitleLanguage.Native, "進撃の巨人")]
    public void EachLanguage_PicksItsOwnTitle(UserTitleLanguage language, string expected)
    {
        var selected = TitleSelector.Select(language, "Shingeki no Kyojin", "Attack on Titan", "進撃の巨人");

        Assert.Equal(expected, selected);
    }

    [Theory]
    // Preferred missing → romaji leads the remainder, except when romaji is the missing one.
    [InlineData(UserTitleLanguage.English, null, "Attack on Titan", "進撃の巨人", "Attack on Titan")]
    [InlineData(UserTitleLanguage.English, "Shingeki no Kyojin", null, "進撃の巨人", "Shingeki no Kyojin")]
    [InlineData(UserTitleLanguage.Native, "Shingeki no Kyojin", "Attack on Titan", null, "Shingeki no Kyojin")]
    [InlineData(UserTitleLanguage.Romaji, null, "Attack on Titan", "進撃の巨人", "Attack on Titan")]
    [InlineData(UserTitleLanguage.Romaji, null, null, "進撃の巨人", "進撃の巨人")]
    [InlineData(UserTitleLanguage.English, null, null, "進撃の巨人", "進撃の巨人")]
    [InlineData(UserTitleLanguage.Native, null, null, null, TitleSelector.UnknownTitle)]
    public void AMissingTitle_FallsThroughTheChain(
        UserTitleLanguage language, string? romaji, string? english, string? native, string expected)
    {
        Assert.Equal(expected, TitleSelector.Select(language, romaji, english, native));
    }

    [Fact]
    public void AMediaWithNoTitlesAtAll_ReadsTheSameEverywhere()
    {
        // RelatedMedia used to say "Unknown" while Media said "Unknown Title" — the drift this
        // consolidation removes. AniList does return media with every title null.
        using var _ = new AppSettingsScope(UserTitleLanguage.Romaji);

        var media = new Media { Id = 1, Title = new MediaTitle() };
        var related = new RelatedMedia { Id = 1, Title = new MediaTitle() };

        Assert.Equal(TitleSelector.UnknownTitle, media.DisplayTitle);
        Assert.Equal(TitleSelector.UnknownTitle, related.DisplayTitle);
    }

    [Theory]
    [InlineData(UserTitleLanguage.Romaji, "Shingeki no Kyojin")]
    [InlineData(UserTitleLanguage.English, "Attack on Titan")]
    public void BothModels_AgreeWithTheSharedChain(UserTitleLanguage language, string expected)
    {
        using var _ = new AppSettingsScope(language);

        var title = new MediaTitle { Romaji = "Shingeki no Kyojin", English = "Attack on Titan", Native = "進撃の巨人" };
        var media = new Media { Id = 1, Title = title };
        var related = new RelatedMedia { Id = 1, Title = title };

        Assert.Equal(expected, media.DisplayTitle);
        Assert.Equal(expected, related.DisplayTitle);
        Assert.Equal(expected, TitleSelector.Select(language, title.Romaji, title.English, title.Native));
    }

    /// <summary>Sets the process-wide title language and puts it back, so ordering can't leak.</summary>
    private sealed class AppSettingsScope : IDisposable
    {
        private readonly UserTitleLanguage _previous;

        public AppSettingsScope(UserTitleLanguage language)
        {
            _previous = AppSettings.TitleLanguage;
            AppSettings.TitleLanguage = language;
        }

        public void Dispose() => AppSettings.TitleLanguage = _previous;
    }
}
