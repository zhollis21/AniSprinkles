using System.ComponentModel;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #127. Every display setting changes what is already rendered on other pages, but no page
/// re-evaluated when the user navigated back. Unlike the adult-content case (#118), these change how
/// already-fetched items render rather than which items exist, so the answer is a re-projection —
/// and it must not cost an AniList request.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class DisplaySettingsReprojectionTests
{
    public DisplaySettingsReprojectionTests() => TestDataBuilder.ResetAppSettings();

    // ── The snapshot ─────────────────────────────────────────────────

    [Fact]
    public void Snapshot_ComparesSectionOrderByContent()
    {
        // The list is joined into a string on purpose: a List<string> member on a record struct
        // compares by reference, so a reordered list would look unchanged.
        AppSettings.AnimeSectionOrder = ["Watching", "Completed"];
        var before = DisplaySettingsSnapshot.Current;

        AppSettings.AnimeSectionOrder = ["Completed", "Watching"];
        var after = DisplaySettingsSnapshot.Current;

        Assert.True(after.SectionOrderDiffersFrom(before));
        Assert.False(after.RenderingDiffersFrom(before));
    }

    [Fact]
    public void Snapshot_TreatsSectionOrderAsIrrelevantToRendering()
    {
        // Section order reorders sections; it does not re-render their contents.
        AppSettings.AnimeSectionOrder = ["Watching"];
        var before = DisplaySettingsSnapshot.Current;

        AppSettings.AnimeSectionOrder = ["Completed"];

        Assert.False(DisplaySettingsSnapshot.Current.RenderingDiffersFrom(before));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Snapshot_ReportsRenderingChanges(bool moveTitleLanguage, bool moveScoreFormat)
    {
        var before = DisplaySettingsSnapshot.Current;

        if (moveTitleLanguage)
        {
            AppSettings.TitleLanguage = UserTitleLanguage.English;
        }

        if (moveScoreFormat)
        {
            AppSettings.ScoreFormat = ScoreFormat.Point5;
        }

        Assert.True(DisplaySettingsSnapshot.Current.RenderingDiffersFrom(before));
    }

    [Fact]
    public void Snapshot_OfUnchangedSettings_ReportsNoDifference()
    {
        var before = DisplaySettingsSnapshot.Current;

        Assert.False(DisplaySettingsSnapshot.Current.RenderingDiffersFrom(before));
        Assert.False(DisplaySettingsSnapshot.Current.SectionOrderDiffersFrom(before));
    }

    // ── The item projections ─────────────────────────────────────────

    [Fact]
    public void RefreshingAnEntry_RaisesTheNestedTitlePathAndTheScoreMembers()
    {
        // Media is a plain class, so the notification has to come from the container for the
        // Media.DisplayTitle binding to re-resolve.
        var entry = TestDataBuilder.Entry(1, title: "Romaji", englishTitle: "English", score: 7);
        var raised = Recorded(entry);

        entry.RefreshDisplayProjections();

        Assert.Contains(nameof(MediaListEntry.Media), raised);
        Assert.Contains(nameof(MediaListEntry.ScoreDisplay), raised);
        Assert.Contains(nameof(MediaListEntry.IsNumericScoreFormat), raised);
    }

    [Fact]
    public void RefreshingACarouselItem_RaisesTheNodePath()
    {
        var item = new BrowseMediaItem { Node = new RelatedMedia() };
        var raised = Recorded(item);

        item.RefreshDisplayProjections();

        Assert.Contains(nameof(BrowseMediaItem.Node), raised);
    }

    [Fact]
    public void RefreshingARecommendation_RaisesItsOwnNestedPath()
    {
        // The odd one out: its RelatedMedia hangs off MediaRecommendation, not Node.
        var node = new MediaRecommendationNode { MediaRecommendation = new RelatedMedia() };
        var raised = Recorded(node);

        node.RefreshDisplayProjections();

        Assert.Contains(nameof(MediaRecommendationNode.MediaRecommendation), raised);
    }

    [Fact]
    public void EveryCarouselContainer_ImplementsTheProjectionContract()
    {
        // A carousel type that forgets the interface renders stale titles forever, and nothing else
        // would catch it — the binding simply never updates.
        Assert.All(
            new object[]
            {
                new MediaListEntry(),
                new BrowseMediaItem(),
                new CharacterMediaEdge(),
                new StaffMediaEdge(),
                new StudioMediaEdge(),
                new MediaRelationEdge(),
                new MediaRecommendationNode(),
                new StaffCharacterEdge(),
            },
            item => Assert.IsAssignableFrom<IDisplayProjection>(item));
    }

    [Fact]
    public void RefreshingAVoiceRole_RaisesTheMediaPathNotTheCharacterOne()
    {
        // The card shows the character's own name over the title of the media they are in, and it is
        // the media title that a display setting reaches.
        var edge = new StaffCharacterEdge { Node = new Character(), Media = new RelatedMedia() };
        var raised = Recorded(edge);

        edge.RefreshDisplayProjections();

        Assert.Contains(nameof(StaffCharacterEdge.Media), raised);
        Assert.DoesNotContain(nameof(StaffCharacterEdge.Node), raised);
    }

    // ── Library, the reported repro ──────────────────────────────────

    [Fact]
    public async Task ChangingTitleLanguage_AndReturningToLibrary_RerendersWithoutRefetching()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var raised = harness.RecordEntryNotifications();

        AppSettings.TitleLanguage = UserTitleLanguage.English;
        await harness.Model.LoadAsync(); // tab back, well inside the five-minute freshness window

        Assert.Contains(nameof(MediaListEntry.Media), raised);
        await harness.Client.Received(1).GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangingScoreFormat_AndReturningToLibrary_RerendersWithoutRefetching()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var raised = harness.RecordEntryNotifications();

        AppSettings.ScoreFormat = ScoreFormat.Point5;
        await harness.Model.LoadAsync();

        Assert.Contains(nameof(MediaListEntry.ScoreDisplay), raised);
        await harness.Client.Received(1).GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturningToLibrary_WithNothingChanged_RaisesNothing()
    {
        // OnAppearing fires on every tab away and back. Re-projecting unconditionally would reset a
        // CollectionView on every tab switch for no reason.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        var raised = harness.RecordEntryNotifications();

        await harness.Model.LoadAsync();

        Assert.Empty(raised);
    }

    [Fact]
    public async Task ChangingTitleLanguage_WhileSortedByTitle_ReordersTheRows()
    {
        // MediaListSection sorts by Media.DisplayTitle, so a language change moves rows as well as
        // re-rendering them. Romaji order is Alpha, Beta; English order is the reverse.
        var harness = new Harness(
            TestDataBuilder.Entry(1, title: "Alpha", englishTitle: "Zulu"),
            TestDataBuilder.Entry(2, title: "Beta", englishTitle: "Yankee"));

        await harness.Model.LoadAsync();
        harness.Model.SelectSortCommand.Execute($"{SortField.Title}:asc");
        Assert.Equal([1, 2], harness.VisibleMediaIds());

        AppSettings.TitleLanguage = UserTitleLanguage.English;
        await harness.Model.LoadAsync();

        Assert.Equal([2, 1], harness.VisibleMediaIds());
        await harness.Client.Received(1).GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangingSectionOrder_AndReturningToLibrary_ReordersWithoutRefetching()
    {
        var harness = new Harness();
        harness.GroupsAre(
            ("Watching", [TestDataBuilder.Entry(1)]),
            ("Completed", [TestDataBuilder.Entry(2)]));

        AppSettings.AnimeSectionOrder = ["Watching", "Completed"];
        await harness.Model.LoadAsync();
        Assert.Equal(["Watching", "Completed"], harness.SectionTitles());

        AppSettings.AnimeSectionOrder = ["Completed", "Watching"];
        await harness.Model.LoadAsync();

        Assert.Equal(["Completed", "Watching"], harness.SectionTitles());
        await harness.Client.Received(1).GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>());
    }

    // ── Media Details, the page that needs both halves ───────────────

    // RefreshDisplaySettings, not a second LoadAsync, is what the page actually calls. Detail pages
    // are pushed onto a tab's navigation stack and get no OnAppearing when that tab becomes current
    // again, so the load never runs on a tab return — only OnNavigatedTo fires. Driving these through
    // LoadAsync passed while the feature was broken on device, because it exercised a call the app
    // never makes.

    [Fact]
    public async Task ChangingScoreFormat_AndReturningToMediaDetails_SwitchesTheRatingControl()
    {
        // Registered transient but kept alive in its tab's navigation stack, so tabbing to Settings
        // and back returns to this same instance rather than a fresh one.
        var model = BuildMediaDetails(out var client);
        await model.LoadAsync(42, listEntry: null);

        var raised = Recorded(model);
        AppSettings.ScoreFormat = ScoreFormat.Point5;
        model.RefreshDisplaySettings();

        Assert.Contains(nameof(MediaDetailsPageModel.ScoreFormatIsStars), raised);
        Assert.Contains(nameof(MediaDetailsPageModel.ScoreFormatIsNumeric), raised);
        Assert.Contains(nameof(MediaDetailsPageModel.NumericScoreMax), raised);
        await client.Received(1).GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangingTitleLanguage_AndReturningToMediaDetails_RerendersTheHeaderAndCarousels()
    {
        var model = BuildMediaDetails(out var client);
        await model.LoadAsync(42, listEntry: null);

        var raised = Recorded(model);
        AppSettings.TitleLanguage = UserTitleLanguage.English;
        model.RefreshDisplaySettings();

        Assert.Contains(nameof(MediaDetailsPageModel.PageTitle), raised);
        await client.Received(1).GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturningToMediaDetails_WithNothingChanged_RaisesNoDisplayNotifications()
    {
        // OnNavigatedTo fires on every tab return and on the initial navigation, so this has to be
        // free when nothing moved.
        var model = BuildMediaDetails(out _);
        await model.LoadAsync(42, listEntry: null);

        var raised = Recorded(model);
        model.RefreshDisplaySettings();

        Assert.DoesNotContain(nameof(MediaDetailsPageModel.ScoreFormatIsStars), raised);
        Assert.DoesNotContain(nameof(MediaDetailsPageModel.PageTitle), raised);
    }

    [Fact]
    public async Task RefreshingDisplaySettingsTwice_OnlyRerendersOnce()
    {
        // The snapshot has to advance, or every subsequent tab return would re-raise for a change
        // that was already applied.
        var model = BuildMediaDetails(out _);
        await model.LoadAsync(42, listEntry: null);

        AppSettings.TitleLanguage = UserTitleLanguage.English;
        model.RefreshDisplaySettings();

        var raised = Recorded(model);
        model.RefreshDisplaySettings();

        Assert.Empty(raised);
    }

    [Fact]
    public async Task ChangingTitleLanguage_AndReturningToMediaDetails_StillWorksViaTheLoadPath()
    {
        // The reuse guard keeps its own call: a sort popup closing does fire OnAppearing, which
        // reaches LoadAsync rather than OnNavigatedTo. Both paths must re-project.
        var model = BuildMediaDetails(out _);
        await model.LoadAsync(42, listEntry: null);

        var raised = Recorded(model);
        AppSettings.TitleLanguage = UserTitleLanguage.English;
        await model.LoadAsync(42, listEntry: null);

        Assert.Contains(nameof(MediaDetailsPageModel.PageTitle), raised);
    }

    private static MediaDetailsPageModel BuildMediaDetails(out IAniListClient client)
    {
        client = Substitute.For<IAniListClient>();
        client.GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(Media?, MediaListEntry?)>((
                new Media
                {
                    Id = 42,
                    Title = new MediaTitle { Romaji = "Romaji", English = "English" },
                },
                null)));

        var dialogs = new ScriptedDialogService();
        return new MediaDetailsPageModel(
            client,
            Substitute.For<IAuthService>(),
            new ErrorReportService(NullLogger<ErrorReportService>.Instance),
            Substitute.For<INavigationService>(),
            new RecordingUserFeedback(),
            new RecordingExternalBrowser(),
            dialogs,
            new ListEntryStatusFlow(dialogs),
            NullLogger<MediaDetailsPageModel>.Instance);
    }

    // ── The shared watcher ───────────────────────────────────────────

    [Fact]
    public void Watcher_RefreshesOnlyWhenTheTitleLanguageMoves()
    {
        var item = new BrowseMediaItem { Node = new RelatedMedia() };
        var raised = Recorded(item);
        var watcher = new TitleProjectionWatcher();

        watcher.RefreshIfTitleLanguageChanged([item]);
        Assert.Empty(raised);

        AppSettings.ScoreFormat = ScoreFormat.Point5;
        watcher.RefreshIfTitleLanguageChanged([item]);
        Assert.Empty(raised); // browse cards show the community average, which no format setting reaches

        AppSettings.TitleLanguage = UserTitleLanguage.English;
        watcher.RefreshIfTitleLanguageChanged([item]);
        Assert.Contains(nameof(BrowseMediaItem.Node), raised);
    }

    [Fact]
    public void Watcher_DoesNotRefreshTwiceForOneChange()
    {
        var item = new BrowseMediaItem { Node = new RelatedMedia() };
        var watcher = new TitleProjectionWatcher();
        watcher.RefreshIfTitleLanguageChanged([item]);

        AppSettings.TitleLanguage = UserTitleLanguage.English;
        watcher.RefreshIfTitleLanguageChanged([item]);

        var raised = Recorded(item);
        watcher.RefreshIfTitleLanguageChanged([item]);

        Assert.Empty(raised);
    }

    [Fact]
    public void Watcher_MarkRendered_SwallowsAChangeTheCallerAlreadyApplied()
    {
        // Called after a load rebuilds the cards, so the next appearance does not redo the work.
        var item = new BrowseMediaItem { Node = new RelatedMedia() };
        var watcher = new TitleProjectionWatcher();

        AppSettings.TitleLanguage = UserTitleLanguage.English;
        watcher.MarkRendered();

        var raised = Recorded(item);
        watcher.RefreshIfTitleLanguageChanged([item]);

        Assert.Empty(raised);
    }

    private static List<string> Recorded(INotifyPropertyChanged source)
    {
        var raised = new List<string>();
        source.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    private sealed class Harness
    {
        private List<(string Name, IReadOnlyList<MediaListEntry> Entries)> _groups;

        public Harness(params MediaListEntry[] entries)
        {
            _groups =
            [
                ("Watching", entries.Length > 0
                    ? entries
                    : [TestDataBuilder.Entry(1, title: "Romaji-1", englishTitle: "English-1", score: 7)]),
            ];

            Client.GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>>(_groups));

            Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(new AniListUser
            {
                Id = 1,
                Name = "zhollis",
                Options = new UserOptions(),
            });

            var preferences = Substitute.For<IPreferences>();
            preferences.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(c => c.ArgAt<string>(1));
            preferences.Get(Arg.Any<string>(), Arg.Any<bool>()).Returns(c => c.ArgAt<bool>(1));
            preferences.Get(Arg.Any<string>(), Arg.Any<int>()).Returns(c => c.ArgAt<int>(1));

            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

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

        public void GroupsAre(params (string Name, IReadOnlyList<MediaListEntry> Entries)[] groups)
            => _groups = [.. groups];

        /// <summary>Subscribes to every loaded entry and collects the property names raised.</summary>
        public List<string> RecordEntryNotifications()
        {
            var raised = new List<string>();
            foreach (var entry in Model.Sections.SelectMany(s => s.AllItems))
            {
                entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
            }

            return raised;
        }

        public List<int> VisibleMediaIds()
            => [.. Model.Sections.SelectMany(s => s).Select(e => e.MediaId)];

        public List<string> SectionTitles() => [.. Model.Sections.Select(s => s.Title)];
    }
}
