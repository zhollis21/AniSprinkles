using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #121. <c>AppSettings</c> persistence used to be unreachable from here: <c>Load</c>, <c>Save</c>,
/// <c>Clear</c> and <c>SyncFromViewer</c> all went through the static <c>Preferences.Default</c>,
/// which throws <c>NotImplementedInReferenceAssemblyException</c> on the plain <c>net11.0</c> TFM
/// these tests build against. With the storage seam in place they round-trip like any other code.
/// </summary>
[NonParallelizable]
public class AppSettingsStorageTests
{
    private readonly FakePreferences _storage;

    public AppSettingsStorageTests() => _storage = TestDataBuilder.ResetAppSettings();

    [Test]
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

        Assert.That(AppSettings.TitleLanguage, Is.EqualTo(UserTitleLanguage.Native));
        Assert.That(AppSettings.ScoreFormat, Is.EqualTo(ScoreFormat.Point5));
        Assert.That(AppSettings.DisplayAdultContent, Is.True);
        Assert.That(AppSettings.AnimeSectionOrder, Is.EqualTo(["Rewatching", "Watching", "Completed"]));
    }

    [Test]
    public void Load_OnAFreshInstall_FallsBackToTheDocumentedDefaults()
    {
        AppSettings.Load();

        Assert.That(AppSettings.TitleLanguage, Is.EqualTo(UserTitleLanguage.Romaji));
        Assert.That(AppSettings.ScoreFormat, Is.EqualTo(ScoreFormat.Point100));

        // Defaults to off: an unconfigured install must not show 18+ results.
        Assert.That(AppSettings.DisplayAdultContent, Is.False);
        Assert.That(AppSettings.AnimeSectionOrder, Is.Empty);
    }

    [Test]
    public void Load_WithAnUnrecognisedStoredEnum_KeepsTheCurrentValueRatherThanThrowing()
    {
        // A downgrade, or an enum member that was renamed, leaves a value the parse cannot resolve.
        // ("Point7" and "Klingon" are deliberately not members of either enum.)
        // Load uses Enum.TryParse precisely so that is survivable.
        _storage.Set("title_language", "Klingon");
        _storage.Set("score_format", "Point7");

        AppSettings.Load();

        Assert.That(AppSettings.TitleLanguage, Is.EqualTo(UserTitleLanguage.Romaji));
        Assert.That(AppSettings.ScoreFormat, Is.EqualTo(ScoreFormat.Point100));
    }

    [Test]
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

        Assert.That(AppSettings.TitleLanguage, Is.EqualTo(UserTitleLanguage.English));
        Assert.That(AppSettings.DisplayAdultContent, Is.True);

        // Persisted, not just assigned — the sync is what makes the choice outlive the process.
        Assert.That(_storage.Get("title_language", string.Empty), Is.EqualTo("English"));
        Assert.That(_storage.Get("display_adult_content", false), Is.True);
        Assert.That(_storage.Get("anime_section_order", string.Empty), Is.EqualTo("Completed"));
    }

    [Test]
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

        Assert.That(AppSettings.TitleLanguage, Is.EqualTo(UserTitleLanguage.Romaji));
        Assert.That(AppSettings.ScoreFormat, Is.EqualTo(ScoreFormat.Point100));
        Assert.That(AppSettings.AnimeSectionOrder, Is.Empty);

        // Sign-out has to take the adult-content preference with it, both in memory and on disk —
        // the next account on this device starts from the safe default.
        Assert.That(AppSettings.DisplayAdultContent, Is.False);
        Assert.That(_storage.ContainsKey("display_adult_content"), Is.False);
        Assert.That(_storage.ContainsKey("title_language"), Is.False);
        Assert.That(_storage.ContainsKey("anime_section_order"), Is.False);
    }

    [Test]
    public void ResetAppSettings_ClearsThePendingMarkers()
    {
        // The markers #128 added are process-wide statics, and the test harness reset did not
        // touch them. A test that changed a setting locally therefore left the marker set, and the
        // next test's SyncFromViewer shadowed the server's value with the stale local one — which
        // is exactly how SettingsPageModelTests failed on CI while passing locally, purely on
        // ordering within the shared collection.
        AppSettings.SetScoreFormat(ScoreFormat.Point5);
        AppSettings.SetTitleLanguage(UserTitleLanguage.Native);
        AppSettings.SetDisplayAdultContent(true);

        TestDataBuilder.ResetAppSettings();

        AppSettings.SyncFromViewer(new AniListUser
        {
            ScoreFormat = ScoreFormat.Point10Decimal,
            Options = new UserOptions
            {
                TitleLanguage = UserTitleLanguage.English,
                DisplayAdultContent = false,
            },
        });

        Assert.That(AppSettings.ScoreFormat, Is.EqualTo(ScoreFormat.Point10Decimal));
        Assert.That(AppSettings.TitleLanguage, Is.EqualTo(UserTitleLanguage.English));
        Assert.That(AppSettings.DisplayAdultContent, Is.False);
    }
}
