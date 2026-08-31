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
    [Fact]
    public void WithNothingStored_TheDefaultIsLarge()
    {
        // Signed-out and first-run users land here, so the default has to be a real choice rather
        // than whatever the enum's zero value happens to be — Standard is the zero value.
        var preferences = new FakePreferences();

        Assert.Equal(ListViewMode.Large, ListViewModePreference.Load(preferences));
    }

    [Theory]
    [InlineData(ListViewMode.Standard)]
    [InlineData(ListViewMode.Large)]
    [InlineData(ListViewMode.Compact)]
    public void EveryMode_SurvivesTheRoundTrip(ListViewMode mode)
    {
        var preferences = new FakePreferences();

        ListViewModePreference.Save(preferences, mode);

        Assert.Equal(mode, ListViewModePreference.Load(preferences));
    }

    [Fact]
    public void AValueThatNoLongerParses_FallsBackToLargeRatherThanThrowing()
    {
        // A mode removed in a later build must not brick the list for anyone who had it selected.
        var preferences = new FakePreferences();
        preferences.Set(ListViewModePreference.Key, "Cinematic");

        Assert.Equal(ListViewMode.Large, ListViewModePreference.Load(preferences));
    }

    [Fact]
    public void TheStoredKeyIsStable()
    {
        // Renaming this silently resets the view mode for every existing install, which looks like
        // a bug to the user and leaves no trace in the logs.
        Assert.Equal("anime_view_mode", ListViewModePreference.Key);
    }

    [Fact]
    public void SavingWritesTheModeNameUnderThatKey()
    {
        var preferences = new FakePreferences();

        ListViewModePreference.Save(preferences, ListViewMode.Compact);

        Assert.Equal("Compact", preferences.Get(ListViewModePreference.Key, string.Empty));
    }

    [Fact]
    public void SavingTheSameModeTwice_StillPersistsBothTimes()
    {
        // Load() reads whatever is last written; there is no dirty-check here, and a page model
        // relying on one would be relying on behaviour this helper does not promise.
        var preferences = new FakePreferences();

        ListViewModePreference.Save(preferences, ListViewMode.Standard);
        ListViewModePreference.Save(preferences, ListViewMode.Standard);

        Assert.Equal(2, preferences.SetCount);
        Assert.Equal(ListViewMode.Standard, ListViewModePreference.Load(preferences));
    }
}
