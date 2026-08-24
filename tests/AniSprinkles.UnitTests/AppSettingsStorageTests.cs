using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #121. <c>AppSettings</c> persistence used to be unreachable from here: <c>Load</c>, <c>Save</c>,
/// <c>Clear</c> and <c>SyncFromViewer</c> all went through the static <c>Preferences.Default</c>,
/// which throws <c>NotImplementedInReferenceAssemblyException</c> on the plain <c>net10.0</c> TFM
/// these tests build against. With the storage seam in place they round-trip like any other code.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class AppSettingsStorageTests
{
    private readonly FakePreferences _storage;

    public AppSettingsStorageTests() => _storage = TestDataBuilder.ResetAppSettings();

    [Fact]
    public void SaveThenLoad_BringsEveryValueBack()
    {
        AppSettings.TitleLanguage = UserTitleLanguage.Native;
        AppSettings.ScoreFormat = ScoreFormat.Point5;
        AppSettings.DisplayAdultContent = true;
        AppSettings.AnimeSectionOrder = ["Rewatching", "Watching", "Completed"];

        AppSettings.Save();

        // Wipe the in-memory statics without touching storage, standing in for a cold start.
        AppSettings.TitleLanguage = UserTitleLanguage.Romaji;
        AppSettings.ScoreFormat = ScoreFormat.Point100;
        AppSettings.DisplayAdultContent = false;
        AppSettings.AnimeSectionOrder = [];

        AppSettings.Load();

        Assert.Equal(UserTitleLanguage.Native, AppSettings.TitleLanguage);
        Assert.Equal(ScoreFormat.Point5, AppSettings.ScoreFormat);
        Assert.True(AppSettings.DisplayAdultContent);
        Assert.Equal(["Rewatching", "Watching", "Completed"], AppSettings.AnimeSectionOrder);
    }

    [Fact]
    public void Load_OnAFreshInstall_FallsBackToTheDocumentedDefaults()
    {
        AppSettings.Load();

        Assert.Equal(UserTitleLanguage.Romaji, AppSettings.TitleLanguage);
        Assert.Equal(ScoreFormat.Point100, AppSettings.ScoreFormat);

        // Defaults to off: an unconfigured install must not show 18+ results.
        Assert.False(AppSettings.DisplayAdultContent);
        Assert.Empty(AppSettings.AnimeSectionOrder);
    }

    [Fact]
    public void Load_WithAnUnrecognisedStoredEnum_KeepsTheCurrentValueRatherThanThrowing()
    {
        // A downgrade, or an enum member that was renamed, leaves a value the parse cannot resolve.
        // ("Point7" and "Klingon" are deliberately not members of either enum.)
        // Load uses Enum.TryParse precisely so that is survivable.
        _storage.Set("title_language", "Klingon");
        _storage.Set("score_format", "Point7");

        AppSettings.Load();

        Assert.Equal(UserTitleLanguage.Romaji, AppSettings.TitleLanguage);
        Assert.Equal(ScoreFormat.Point100, AppSettings.ScoreFormat);
    }

    [Fact]
    public void SyncFromViewer_AppliesTheProfileAndPersistsIt()
    {
        AppSettings.SyncFromViewer(new AniListUser
        {
            ScoreFormat = ScoreFormat.Point10,
            AnimeSectionOrder = ["Completed"],
            Options = new UserOptions
            {
                TitleLanguage = UserTitleLanguage.English,
                DisplayAdultContent = true,
            },
        });

        Assert.Equal(UserTitleLanguage.English, AppSettings.TitleLanguage);
        Assert.True(AppSettings.DisplayAdultContent);

        // Persisted, not just assigned — the sync is what makes the choice outlive the process.
        Assert.Equal("English", _storage.Get("title_language", string.Empty));
        Assert.True(_storage.Get("display_adult_content", false));
        Assert.Equal("Completed", _storage.Get("anime_section_order", string.Empty));
    }

    [Fact]
    public void Clear_ResetsTheStaticsAndRemovesTheStoredKeys()
    {
        AppSettings.SyncFromViewer(new AniListUser
        {
            ScoreFormat = ScoreFormat.Point10,
            AnimeSectionOrder = ["Completed"],
            Options = new UserOptions
            {
                TitleLanguage = UserTitleLanguage.English,
                DisplayAdultContent = true,
            },
        });

        AppSettings.Clear();

        Assert.Equal(UserTitleLanguage.Romaji, AppSettings.TitleLanguage);
        Assert.Equal(ScoreFormat.Point100, AppSettings.ScoreFormat);
        Assert.Empty(AppSettings.AnimeSectionOrder);

        // Sign-out has to take the adult-content preference with it, both in memory and on disk —
        // the next account on this device starts from the safe default.
        Assert.False(AppSettings.DisplayAdultContent);
        Assert.False(_storage.ContainsKey("display_adult_content"));
        Assert.False(_storage.ContainsKey("title_language"));
        Assert.False(_storage.ContainsKey("anime_section_order"));
    }
}
