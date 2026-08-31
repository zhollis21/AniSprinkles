using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 1, the last uncovered slice of <see cref="AnimeLibraryPageModel"/>: the search filter and the
/// sort picker. List loading, section ordering and the +1 debounce are covered elsewhere
/// (<see cref="MediaListAdultFilterReloadTests"/>, <see cref="MediaListSortDefinitionsTests"/>,
/// <see cref="MediaListSectionsMergerTests"/>, <see cref="MediaListPageModelTests"/>).
/// <para>
/// The sort path is the interesting half. <c>SelectSort</c> writes field and direction as two
/// separate observable properties, each with a handler that persists and re-sorts — so it suppresses
/// the per-property re-sort and applies one afterwards. Without that, every pick sorts twice, the
/// first time with the new field and the previous direction.
/// </para>
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class MediaListFilterAndSortTests
{
    public MediaListFilterAndSortTests() => TestDataBuilder.ResetAppSettings();

    // ── The search filter ────────────────────────────────────────────

    [Fact]
    public async Task Typing_NarrowsTheSectionToMatchingEntries()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        var section = harness.Model.Sections[0];
        Assert.Equal(3, section.FilteredCount);

        harness.Model.SearchText = "Bocchi";

        Assert.Equal(1, section.FilteredCount);
        var only = Assert.Single(section);
        Assert.NotNull(only.Media);
        Assert.Equal("Bocchi the Rock", only.Media.DisplayTitle);
    }

    [Fact]
    public async Task TheFilterIsCaseInsensitive()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SearchText = "bOcChI";

        Assert.Equal(1, harness.Model.Sections[0].FilteredCount);
    }

    [Fact]
    public async Task TheFilterMatchesTheEnglishTitleToo()
    {
        // The card renders whichever title language the viewer picked, but a search that only
        // consulted Romaji would fail to find a show the user is looking straight at.
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SearchText = "Solo Leveling";

        Assert.Equal(1, harness.Model.Sections[0].FilteredCount);
    }

    [Fact]
    public async Task ClearingTheText_RestoresEveryEntry()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.Model.SearchText = "Bocchi";

        harness.Model.SearchText = string.Empty;

        Assert.Equal(3, harness.Model.Sections[0].FilteredCount);
        Assert.False(harness.Model.Sections[0].IsFiltered);
    }

    [Fact]
    public async Task AFilteredSection_ShowsBothCountsOnItsBadge()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SearchText = "Bocchi";

        Assert.True(harness.Model.Sections[0].IsFiltered);
        Assert.Equal("1/3", harness.Model.Sections[0].DisplayCount);
    }

    [Fact]
    public async Task WithNothingMatching_TheNoResultsStateAppears()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.Model.ToggleSearchCommand.Execute(null);

        harness.Model.SearchText = "nothing matches this";

        Assert.True(harness.Model.HasNoResults);
    }

    [Fact]
    public async Task WithSomethingMatching_TheNoResultsStateStaysHidden()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.Model.ToggleSearchCommand.Execute(null);

        harness.Model.SearchText = "Bocchi";

        Assert.False(harness.Model.HasNoResults);
    }

    [Fact]
    public async Task AnEmptyQuery_IsNotNoResultsEvenWithTheSearchBarOpen()
    {
        // Opening the search bar must not immediately accuse the library of being empty.
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.ToggleSearchCommand.Execute(null);

        Assert.True(harness.Model.IsSearchVisible);
        Assert.False(harness.Model.HasNoResults);
    }

    [Fact]
    public async Task AQueryTypedWithTheBarClosed_IsNotNoResults()
    {
        // HasNoResults drives an on-screen message that belongs to the search UI.
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SearchText = "nothing matches this";

        Assert.False(harness.Model.IsSearchVisible);
        Assert.False(harness.Model.HasNoResults);
    }

    [Fact]
    public async Task ChangingTheQuery_NotifiesTheNoResultsBinding()
    {
        // HasNoResults is computed, so nothing tells the view about it unless the handler says so.
        var harness = new Harness();
        await harness.LoadAsync();
        var notified = false;
        harness.Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AnimeLibraryPageModel.HasNoResults))
            {
                notified = true;
            }
        };

        harness.Model.SearchText = "Bocchi";

        Assert.True(notified);
    }

    [Fact]
    public async Task ClosingTheSearchBar_ClearsTheQueryAndTheFilter()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.Model.ToggleSearchCommand.Execute(null);
        harness.Model.SearchText = "Bocchi";

        harness.Model.ToggleSearchCommand.Execute(null);

        Assert.False(harness.Model.IsSearchVisible);
        Assert.Equal(string.Empty, harness.Model.SearchText);
        Assert.Equal(3, harness.Model.Sections[0].FilteredCount);
    }

    // ── The sort picker ──────────────────────────────────────────────

    [Fact]
    public async Task SelectingASort_AppliesBothHalvesOfTheCode()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SelectSortCommand.Execute("Title:asc");

        Assert.Equal(SortField.Title, harness.Model.CurrentSortField);
        Assert.True(harness.Model.SortAscending);
        Assert.Equal("Title:asc", harness.Model.SelectedSortCode);
    }

    [Fact]
    public async Task SelectingASort_ReordersTheEntriesOnScreen()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SelectSortCommand.Execute("Title:asc");

        Assert.Equal(
            ["Bocchi the Rock", "Frieren", "Ore dake Level Up na Ken"],
            harness.Model.Sections[0].Select(e => e.Media!.DisplayTitle));
    }

    [Fact]
    public async Task ReversingTheDirection_ReversesTheOrder()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        harness.Model.SelectSortCommand.Execute("Title:asc");

        harness.Model.SelectSortCommand.Execute("Title:desc");

        Assert.Equal(
            ["Ore dake Level Up na Ken", "Frieren", "Bocchi the Rock"],
            harness.Model.Sections[0].Select(e => e.Media!.DisplayTitle));
    }

    [Fact]
    public async Task SortingComposesWithAnActiveFilter()
    {
        // The filter narrows and the sort orders what is left; neither may discard the other.
        var harness = new Harness();
        await harness.LoadAsync();
        harness.Model.SearchText = "e";

        harness.Model.SelectSortCommand.Execute("Title:asc");

        Assert.Equal(
            ["Bocchi the Rock", "Frieren", "Ore dake Level Up na Ken"],
            harness.Model.Sections[0].Select(e => e.Media!.DisplayTitle));
    }

    [Fact]
    public async Task SelectingTheActiveSortAgain_ChangesAndPersistsNothing()
    {
        // The picker stays open on the current row, so re-tapping it is the common gesture.
        var harness = new Harness();
        await harness.LoadAsync();
        var writesBefore = harness.Preferences.SetCount;

        harness.Model.SelectSortCommand.Execute(harness.Model.SelectedSortCode);

        Assert.Equal(writesBefore, harness.Preferences.SetCount);
    }

    [Fact]
    public async Task SelectingASort_PersistsBothHalves()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SelectSortCommand.Execute("Score:asc");

        Assert.Equal("Score", harness.Preferences.Get("anime_sort_field", string.Empty));
        Assert.True(harness.Preferences.Get("anime_sort_ascending", false));
    }

    [Fact]
    public async Task ChangingOnlyTheDirection_LeavesTheFieldAlone()
    {
        var harness = new Harness();
        await harness.LoadAsync();
        Assert.Equal("LastUpdated:desc", harness.Model.SelectedSortCode);

        harness.Model.SelectSortCommand.Execute("LastUpdated:asc");

        Assert.Equal(SortField.LastUpdated, harness.Model.CurrentSortField);
        Assert.True(harness.Model.SortAscending);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("Title")]
    [InlineData("Title:sideways")]
    [InlineData("NotAField:asc")]
    [InlineData("Title:asc:extra")]
    public async Task AMalformedCode_IsIgnoredRatherThanApplied(string? code)
    {
        // The picker only ever emits valid codes, so anything else is a wiring bug — it must not
        // leave the list sorted by something nobody asked for.
        var harness = new Harness();
        await harness.LoadAsync();
        var before = harness.Model.SelectedSortCode;

        harness.Model.SelectSortCommand.Execute(code);

        Assert.Equal(before, harness.Model.SelectedSortCode);
    }

    [Fact]
    public async Task EveryCodeThePickerOffers_IsAccepted()
    {
        // The build-time guard in MediaListSortDefinitionsTests says the codes parse; this says the
        // command actually applies each one, so the two cannot drift.
        var harness = new Harness();
        await harness.LoadAsync();

        foreach (var option in harness.Model.SortOptions)
        {
            harness.Model.SelectSortCommand.Execute(option.Code);

            Assert.Equal(option.Code, harness.Model.SelectedSortCode);
        }
    }

    [Fact]
    public async Task TheActiveSort_IsTheOnlyHighlightedRow()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SelectSortCommand.Execute("Score:desc");

        var selected = Assert.Single(harness.Model.SortOptions, o => o.IsSelected);
        Assert.Equal("Score:desc", selected.Code);
    }

    [Fact]
    public async Task TheSortGlyph_TracksTheDirection()
    {
        var harness = new Harness();
        await harness.LoadAsync();

        harness.Model.SelectSortCommand.Execute("Title:asc");
        var ascending = harness.Model.SortIconGlyph;
        harness.Model.SelectSortCommand.Execute("Title:desc");

        Assert.NotEqual(ascending, harness.Model.SortIconGlyph);
    }

    private sealed class Harness
    {
        public Harness()
        {
            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

            Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(new AniListUser
            {
                Id = 1,
                Name = "zhollis",
                Options = new UserOptions { DisplayAdultContent = true },
            });

            // One group, so it is the first section and therefore expanded — the visible collection
            // is what the ordering assertions read.
            Client.GetMediaListGroupedAsync(Arg.Any<MediaKind>(), Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>>(
                [
                    ("Watching", new List<MediaListEntry>
                    {
                        TestDataBuilder.Entry(1, title: "Frieren", score: 9, updatedAt: DateTimeOffset.UnixEpoch.AddDays(2)),
                        TestDataBuilder.Entry(2, title: "Bocchi the Rock", score: 8, updatedAt: DateTimeOffset.UnixEpoch.AddDays(3)),
                        TestDataBuilder.Entry(3, title: "Ore dake Level Up na Ken", englishTitle: "Solo Leveling", score: 7, updatedAt: DateTimeOffset.UnixEpoch.AddDays(1)),
                    })
                ]));

            var dialogs = new ScriptedDialogService();
            Model = new AnimeLibraryPageModel(
                Client,
                auth,
                Substitute.For<IAiringNotificationService>(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Preferences,
                Substitute.For<INavigationService>(),
                dialogs,
                new RecordingUserFeedback(),
                new ListEntryStatusFlow(dialogs),
                new ManualTimeProvider(DateTimeOffset.UnixEpoch),
                NullLogger<AnimeLibraryPageModel>.Instance);
        }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public FakePreferences Preferences { get; } = new();

        public AnimeLibraryPageModel Model { get; }

        public Task LoadAsync() => Model.LoadAsync();
    }
}
