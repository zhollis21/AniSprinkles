using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 small-helpers pass for <see cref="ListViewModePreference"/>, the one device-wide list look
/// shared by Library and the media-browse (View All) lists. Both page models read and write it, so
/// the storage key and the round trip are a contract between them rather than an implementation
/// detail of either.
/// </summary>
public class ListViewModePreferenceTests
{
    [Test]
    public void WithNothingStored_TheDefaultIsLarge()
    {
        // Signed-out and first-run users land here, so the default has to be a real choice rather
        // than whatever the enum's zero value happens to be — Standard is the zero value.
        var preferences = new FakePreferences();

        Assert.That(ListViewModePreference.Load(preferences), Is.EqualTo(ListViewMode.Large));
    }

    [TestCase(ListViewMode.Standard)]
    [TestCase(ListViewMode.Large)]
    [TestCase(ListViewMode.Compact)]
    public void EveryMode_SurvivesTheRoundTrip(ListViewMode mode)
    {
        var preferences = new FakePreferences();

        ListViewModePreference.Save(preferences, mode);

        Assert.That(ListViewModePreference.Load(preferences), Is.EqualTo(mode));
    }

    [Test]
    public void AValueThatNoLongerParses_FallsBackToLargeRatherThanThrowing()
    {
        // A mode removed in a later build must not brick the list for anyone who had it selected.
        var preferences = new FakePreferences();
        preferences.Set(ListViewModePreference.Key, "Cinematic");

        Assert.That(ListViewModePreference.Load(preferences), Is.EqualTo(ListViewMode.Large));
    }

    [Test]
    public void TheStoredKeyIsStable()
    {
        // Renaming this silently resets the view mode for every existing install, which looks like
        // a bug to the user and leaves no trace in the logs.
        Assert.That(ListViewModePreference.Key, Is.EqualTo("anime_view_mode"));
    }

    [Test]
    public void SavingWritesTheModeNameUnderThatKey()
    {
        var preferences = new FakePreferences();

        ListViewModePreference.Save(preferences, ListViewMode.Compact);

        Assert.That(preferences.Get(ListViewModePreference.Key, string.Empty), Is.EqualTo("Compact"));
    }

    [Test]
    public void SavingTheSameModeTwice_StillPersistsBothTimes()
    {
        // Load() reads whatever is last written; there is no dirty-check here, and a page model
        // relying on one would be relying on behaviour this helper does not promise.
        var preferences = new FakePreferences();

        ListViewModePreference.Save(preferences, ListViewMode.Standard);
        ListViewModePreference.Save(preferences, ListViewMode.Standard);

        Assert.That(preferences.SetCount, Is.EqualTo(2));
        Assert.That(ListViewModePreference.Load(preferences), Is.EqualTo(ListViewMode.Standard));
    }
}
