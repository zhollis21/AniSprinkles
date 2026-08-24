using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #118, fourth surface. Discover, Search and View All each compare the adult-content setting
/// against the value their current results were loaded under, so returning to them after a change
/// refetches. Library did not: its short-circuit weighed only auth and a five-minute freshness
/// window, so turning 18+ content off and tabbing back left the entries on screen — and unlike the
/// other three, tabbing away and back again did not clear it either.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class MyAnimeAdultFilterReloadTests
{
    public MyAnimeAdultFilterReloadTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task ReturningToLibrary_AfterAdultContentIsTurnedOff_DropsTheAdultEntries()
    {
        var harness = new Harness();
        harness.ViewerReports(displayAdultContent: true);
        await harness.Model.LoadAsync();
        Assert.Contains(harness.AllEntries(), e => e.Media?.IsAdult == true);

        // The user turns 18+ content off on Settings, which commits immediately (#118), and taps
        // back to Library well inside the five-minute freshness window.
        harness.ViewerReports(displayAdultContent: false);
        await harness.Model.LoadAsync();

        Assert.DoesNotContain(harness.AllEntries(), e => e.Media?.IsAdult == true);
        Assert.Contains(harness.AllEntries(), e => e.MediaId == 1);
    }

    [Fact]
    public async Task ReturningToLibrary_AfterAdultContentIsTurnedOn_BringsThemBack()
    {
        var harness = new Harness();
        harness.ViewerReports(displayAdultContent: false);
        await harness.Model.LoadAsync();
        Assert.DoesNotContain(harness.AllEntries(), e => e.Media?.IsAdult == true);

        harness.ViewerReports(displayAdultContent: true);
        await harness.Model.LoadAsync();

        Assert.Contains(harness.AllEntries(), e => e.Media?.IsAdult == true);
    }

    [Fact]
    public async Task ReturningToLibrary_InsideTheSaveDebounce_DoesNotRevertTheLocalChange()
    {
        // The regression this pairs with: fixing the short-circuit above made Library reload exactly
        // when the setting had just changed — and its load syncs display preferences from the
        // viewer, whose copy is still the old one until the debounced save lands. Without the
        // pending guard in AppSettings, tabbing to Library right after turning 18+ off turned it
        // back on app-wide.
        var harness = new Harness();
        harness.ViewerReports(displayAdultContent: true);
        await harness.Model.LoadAsync();

        // Toggled off on Settings: committed locally, not yet sent. The viewer still says on.
        AppSettings.SetDisplayAdultContent(false);

        await harness.Model.LoadAsync();

        Assert.False(AppSettings.DisplayAdultContent);
        Assert.DoesNotContain(harness.AllEntries(), e => e.Media?.IsAdult == true);
    }

    [Fact]
    public async Task ReturningToLibrary_WithTheSettingUnchanged_StillShortCircuits()
    {
        // The freshness window is what keeps tab switching snappy on a large list. Adding the
        // adult-content comparison must not cost a refetch on every appearance.
        var harness = new Harness();
        harness.ViewerReports(displayAdultContent: true);
        await harness.Model.LoadAsync();

        await harness.Model.LoadAsync();

        await harness.Client.Received(1).GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        public Harness()
        {
            var preferences = Substitute.For<IPreferences>();
            preferences.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(c => c.ArgAt<string>(1));
            preferences.Get(Arg.Any<string>(), Arg.Any<bool>()).Returns(c => c.ArgAt<bool>(1));
            preferences.Get(Arg.Any<string>(), Arg.Any<int>()).Returns(c => c.ArgAt<int>(1));

            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

            Client.GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>>(
                [
                    ("Watching", new List<MediaListEntry>
                    {
                        TestDataBuilder.Entry(1),
                        TestDataBuilder.Entry(2, isAdult: true),
                    })
                ]));

            var dialogs = new ScriptedDialogService();
            Model = new MyAnimePageModel(
                Client,
                auth,
                Substitute.For<IAiringNotificationService>(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                preferences,
                Substitute.For<INavigationService>(),
                dialogs,
                new RecordingUserFeedback(),
                new ListEntryStatusFlow(dialogs),
                new ManualTimeProvider(DateTimeOffset.UnixEpoch),
                NullLogger<MyAnimePageModel>.Instance);
        }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public MyAnimePageModel Model { get; }

        /// <summary>
        /// Points both the viewer response and <c>AppSettings</c> at the same value. LoadAsync syncs
        /// display preferences from the viewer before building sections, so a test that moved only
        /// the static would have it overwritten before the filter ever ran.
        /// </summary>
        public void ViewerReports(bool displayAdultContent)
        {
            AppSettings.DisplayAdultContent = displayAdultContent;
            Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(new AniListUser
            {
                Id = 1,
                Name = "zhollis",
                Options = new UserOptions { DisplayAdultContent = displayAdultContent },
            });
        }

        // AllItems, not the collection itself: adult filtering removes entries at group-build time,
        // whereas the collection surface reflects the search-text filter on top of that.
        public IEnumerable<MediaListEntry> AllEntries()
            => Model.Sections.SelectMany(s => s.AllItems);
    }
}
