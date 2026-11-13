namespace AniSprinkles.UnitTests;

/// <summary>
/// #141. The title-language fallback chain existed in three places — <c>Media.DisplayTitle</c>,
/// <c>RelatedMedia.DisplayTitle</c>, and the airing worker's own copy, whose doc comment claimed it
/// matched the app UI with nothing checking that. These pin the one shared chain, and that the two
/// model properties now route through it.
/// </summary>
[NonParallelizable]
public class TitleSelectorTests
{
    [TestCase(UserTitleLanguage.Romaji, "Shingeki no Kyojin")]
    [TestCase(UserTitleLanguage.English, "Attack on Titan")]
    [TestCase(UserTitleLanguage.Native, "進撃の巨人")]
    public void EachLanguage_PicksItsOwnTitle(UserTitleLanguage language, string expected)
    {
        var selected = TitleSelector.Select(language, "Shingeki no Kyojin", "Attack on Titan", "進撃の巨人");

        Assert.That(selected, Is.EqualTo(expected));
    }

    [TestCase(UserTitleLanguage.English, null, "Attack on Titan", "進撃の巨人", "Attack on Titan")]
    [TestCase(UserTitleLanguage.English, "Shingeki no Kyojin", null, "進撃の巨人", "Shingeki no Kyojin")]
    [TestCase(UserTitleLanguage.Native, "Shingeki no Kyojin", "Attack on Titan", null, "Shingeki no Kyojin")]
    [TestCase(UserTitleLanguage.Romaji, null, "Attack on Titan", "進撃の巨人", "Attack on Titan")]
    [TestCase(UserTitleLanguage.Romaji, null, null, "進撃の巨人", "進撃の巨人")]
    [TestCase(UserTitleLanguage.English, null, null, "進撃の巨人", "進撃の巨人")]
    [TestCase(UserTitleLanguage.Native, null, null, null, TitleSelector.UnknownTitle)]
    public void AMissingTitle_FallsThroughTheChain(
        UserTitleLanguage language, string? romaji, string? english, string? native, string expected)
    {
        Assert.That(TitleSelector.Select(language, romaji, english, native), Is.EqualTo(expected));
    }

    [Test]
    public void AMediaWithNoTitlesAtAll_ReadsTheSameEverywhere()
    {
        // RelatedMedia used to say "Unknown" while Media said "Unknown Title" — the drift this
        // consolidation removes. AniList does return media with every title null.
        using var _ = new AppSettingsScope(UserTitleLanguage.Romaji);

        var media = new Media { Id = 1, Title = new MediaTitle() };
        var related = new RelatedMedia { Id = 1, Title = new MediaTitle() };

        Assert.That(media.DisplayTitle, Is.EqualTo(TitleSelector.UnknownTitle));
        Assert.That(related.DisplayTitle, Is.EqualTo(TitleSelector.UnknownTitle));
    }

    [TestCase(UserTitleLanguage.Romaji, "Shingeki no Kyojin")]
    [TestCase(UserTitleLanguage.English, "Attack on Titan")]
    public void BothModels_AgreeWithTheSharedChain(UserTitleLanguage language, string expected)
    {
        using var _ = new AppSettingsScope(language);

        var title = new MediaTitle { Romaji = "Shingeki no Kyojin", English = "Attack on Titan", Native = "進撃の巨人" };
        var media = new Media { Id = 1, Title = title };
        var related = new RelatedMedia { Id = 1, Title = title };

        Assert.That(media.DisplayTitle, Is.EqualTo(expected));
        Assert.That(related.DisplayTitle, Is.EqualTo(expected));
        Assert.That(TitleSelector.Select(language, title.Romaji, title.English, title.Native), Is.EqualTo(expected));
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
