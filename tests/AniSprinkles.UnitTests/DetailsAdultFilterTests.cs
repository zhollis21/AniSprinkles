using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #199. AniList returns 18+ titles inside every nested media connection on the details pages
/// (voice roles, production roles, appearances, studio productions, relations, recommendations)
/// whether or not the viewer is signed in. None of those connections takes an <c>isAdult</c>
/// argument, so the server cannot be asked to filter them. With Display Adult Content off, the app
/// has to drop them itself, the way the Library does (<c>MediaListSectionsMerger</c>).
/// </summary>
[NonParallelizable]
public class DetailsAdultFilterTests
{
    public DetailsAdultFilterTests() => TestDataBuilder.ResetAppSettings();

    // ── Staff: voice roles ───────────────────────────────────────────

    [Test]
    public async Task VoiceRole_WithAnAdultTitleFirst_ShowsItsFirstNonAdultTitle()
    {
        // The case CI caught: the card renders one title, and it used to be whichever came first.
        AppSettings.DisplayAdultContent = false;
        var staff = StaffHarness.With(voiceRoles: [Role(1, Media(10, adult: true), Media(11))]);

        await staff.Model.LoadAsync(42);

        Assert.That(staff.Model.DisplayedVoiceRoles.Single().Media?.Id, Is.EqualTo(11));
    }

    [Test]
    public async Task VoiceRole_WithOnlyAdultTitles_IsDropped()
    {
        AppSettings.DisplayAdultContent = false;
        var staff = StaffHarness.With(voiceRoles: [Role(1, Media(10, adult: true)), Role(2, Media(20))]);

        await staff.Model.LoadAsync(42);

        Assert.That(staff.Model.DisplayedVoiceRoles.Select(r => r.Node?.Id), Is.EqualTo([2]));
    }

    [Test]
    public async Task VoiceRole_WithAdultContentOn_KeepsItsFirstTitle()
    {
        AppSettings.DisplayAdultContent = true;
        var staff = StaffHarness.With(voiceRoles: [Role(1, Media(10, adult: true), Media(11))]);

        await staff.Model.LoadAsync(42);

        Assert.That(staff.Model.DisplayedVoiceRoles.Single().Media?.Id, Is.EqualTo(10));
    }

    [Test]
    public async Task VoiceRole_Filtering_DoesNotRewriteTheFetchedEdge()
    {
        // Staff reads are cached for the session (CachingAniListClient), so the edge the page is
        // handed is the cached one. Rewriting it in place would leave the cache showing the
        // non-adult title after adult content is turned back on.
        AppSettings.DisplayAdultContent = false;
        var fetched = Role(1, Media(10, adult: true), Media(11));
        var staff = StaffHarness.With(voiceRoles: [fetched]);

        await staff.Model.LoadAsync(42);

        Assert.That(fetched.Media?.Id, Is.EqualTo(10));
    }

    [Test]
    public async Task VoiceRoles_LoadMore_DropsAdultRolesToo()
    {
        AppSettings.DisplayAdultContent = false;
        var staff = StaffHarness.With(voiceRoles: [Role(1, Media(10))], moreVoiceRoles: true);
        staff.Client
            .LoadStaffCharactersPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StaffCharacterEdge>, PageInfo?)>(
                ([Role(2, Media(20, adult: true)), Role(3, Media(30))], new PageInfo { CurrentPage = 2, HasNextPage = false })));
        await staff.Model.LoadAsync(42);

        await staff.Model.LoadMoreVoiceRolesCommand.ExecuteAsync(null);

        Assert.That(staff.Model.DisplayedVoiceRoles.Select(r => r.Node?.Id), Is.EqualTo([1, 3]));
    }

    [Test]
    public async Task VoiceRoles_LoadMore_UsesTheValuePageOneWasLoadedUnder()
    {
        // One list, one policy. A Load More that re-read the setting after it moved would append
        // 18+ roles under a page that was built without them. The rebuild on return is what moves
        // the page to the new value, not paging.
        AppSettings.DisplayAdultContent = false;
        var staff = StaffHarness.With(voiceRoles: [Role(1, Media(10))], moreVoiceRoles: true);
        staff.Client
            .LoadStaffCharactersPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StaffCharacterEdge>, PageInfo?)>(
                ([Role(2, Media(20, adult: true))], new PageInfo { CurrentPage = 2, HasNextPage = false })));
        await staff.Model.LoadAsync(42);

        AppSettings.DisplayAdultContent = true;
        await staff.Model.LoadMoreVoiceRolesCommand.ExecuteAsync(null);

        Assert.That(staff.Model.DisplayedVoiceRoles.Select(r => r.Node?.Id), Is.EqualTo([1]));
    }

    // ── Staff: production roles ──────────────────────────────────────

    [Test]
    public async Task ProductionRole_OnAnAdultTitle_IsDropped()
    {
        AppSettings.DisplayAdultContent = false;
        var staff = StaffHarness.With(productionRoles:
        [
            new StaffMediaEdge { Node = Media(10, adult: true), StaffRole = "Director" },
            new StaffMediaEdge { Node = Media(11), StaffRole = "Director" },
        ]);

        await staff.Model.LoadAsync(42);

        Assert.That(staff.Model.DisplayedProductionRoles.Select(r => r.Node?.Id), Is.EqualTo([11]));
    }

    [Test]
    public async Task ProductionRoles_SortRefetch_DropsAdultRolesToo()
    {
        AppSettings.DisplayAdultContent = false;
        var staff = StaffHarness.With(
            productionRoles: [new StaffMediaEdge { Node = Media(11), StaffRole = "Director" }],
            moreProductionRoles: true);
        staff.Client
            .LoadStaffMediaPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StaffMediaEdge>, PageInfo?)>(
                ([new StaffMediaEdge { Node = Media(20, adult: true), StaffRole = "Director" },
                  new StaffMediaEdge { Node = Media(21), StaffRole = "Director" }],
                 new PageInfo { CurrentPage = 1, HasNextPage = false })));
        await staff.Model.LoadAsync(42);

        await staff.Model.SelectProductionRolesSortCommand.ExecuteAsync("SCORE_DESC");

        Assert.That(staff.Model.DisplayedProductionRoles.Select(r => r.Node?.Id), Is.EqualTo([21]));
    }

    // ── Character, Studio, Media ─────────────────────────────────────

    [Test]
    public async Task CharacterAppearance_InAnAdultTitle_IsDropped()
    {
        AppSettings.DisplayAdultContent = false;
        var harness = new CharacterHarness();
        var character = new Character { Id = 7, MediaPageInfo = new PageInfo { CurrentPage = 1 } };
        character.Media.Add(new CharacterMediaEdge { Node = Media(10, adult: true), CharacterRole = "MAIN" });
        character.Media.Add(new CharacterMediaEdge { Node = Media(11), CharacterRole = "MAIN" });
        harness.Returns(character);

        await harness.Model.LoadAsync(7);

        Assert.That(harness.Model.DisplayedAppearances.Select(a => a.Node?.Id), Is.EqualTo([11]));
    }

    [Test]
    public async Task StudioProduction_OfAnAdultTitle_IsDropped()
    {
        AppSettings.DisplayAdultContent = false;
        var harness = new StudioHarness();
        harness.Returns(new Studio
        {
            Id = 3,
            Media = [new StudioMediaEdge { Node = Media(10, adult: true) }, new StudioMediaEdge { Node = Media(11) }],
            MediaPageInfo = new PageInfo { CurrentPage = 1 },
        });

        await harness.Model.LoadAsync(3);

        Assert.That(harness.Model.DisplayedProductions.Select(p => p.Node?.Id), Is.EqualTo([11]));
    }

    [Test]
    public async Task MediaRelationsAndRecommendations_OfAdultTitles_AreDropped()
    {
        AppSettings.DisplayAdultContent = false;
        var harness = new MediaHarness();
        harness.Returns(new Media
        {
            Id = 5,
            Relations =
            [
                new MediaRelationEdge { RelationType = "SEQUEL", Node = Media(10, adult: true) },
                new MediaRelationEdge { RelationType = "PREQUEL", Node = Media(11) },
            ],
            Recommendations =
            [
                new MediaRecommendationNode { Rating = 5, MediaRecommendation = Media(20, adult: true) },
                new MediaRecommendationNode { Rating = 4, MediaRecommendation = Media(21) },
            ],
            RecommendationsPageInfo = new PageInfo { CurrentPage = 1 },
        });

        await harness.Model.LoadAsync(5, listEntry: null);

        Assert.That(harness.Model.DisplayedRelations.Select(r => r.Node?.Id), Is.EqualTo([11]));
        Assert.That(harness.Model.DisplayedRecommendations.Select(r => r.MediaRecommendation?.Id), Is.EqualTo([21]));
    }

    [Test]
    public async Task NestedMedia_WithNoAdultFlag_IsKept()
    {
        // Only an explicit true hides a title. A query that does not ask for the flag must not
        // empty the page.
        AppSettings.DisplayAdultContent = false;
        var staff = StaffHarness.With(productionRoles: [new StaffMediaEdge { Node = new RelatedMedia { Id = 10 }, StaffRole = "Director" }]);

        await staff.Model.LoadAsync(42);

        Assert.That(staff.Model.DisplayedProductionRoles, Has.Exactly(1).Items);
    }

    // ── Returning to a page after the setting moved ──────────────────

    [Test]
    public async Task ReturningToThePage_AfterAdultContentIsTurnedOff_RebuildsWithoutTheAdultTitles()
    {
        // Details pages stay alive in their tab's stack. Turning adult content off on Settings and
        // tabbing back lands on the same instance, and the same-id short-circuit used to keep the
        // sections it had built with adult content on.
        AppSettings.DisplayAdultContent = true;
        var staff = StaffHarness.With(voiceRoles: [Role(1, Media(10, adult: true)), Role(2, Media(20))]);
        await staff.Model.LoadAsync(42);
        Assert.That(staff.Model.DisplayedVoiceRoles, Has.Exactly(2).Items);

        AppSettings.DisplayAdultContent = false;
        staff.Model.RefreshDisplaySettings();
        await staff.Model.LoadAsync(42);

        Assert.That(staff.Model.DisplayedVoiceRoles.Select(r => r.Node?.Id), Is.EqualTo([2]));
    }

    [Test]
    public async Task ReturningToThePage_WhenTitleLanguageMovedToo_StillRebuilds()
    {
        // Both settings moving at once is the trap: the title-language re-projection runs first
        // (from OnNavigatedTo) and records the settings as rendered. If the adult setting were
        // tracked in that same snapshot, the load that follows would see nothing left to do.
        AppSettings.DisplayAdultContent = true;
        var staff = StaffHarness.With(voiceRoles: [Role(1, Media(10, adult: true)), Role(2, Media(20))]);
        await staff.Model.LoadAsync(42);

        AppSettings.DisplayAdultContent = false;
        AppSettings.TitleLanguage = AppSettings.TitleLanguage == UserTitleLanguage.English
            ? UserTitleLanguage.Romaji
            : UserTitleLanguage.English;
        staff.Model.RefreshDisplaySettings();
        await staff.Model.LoadAsync(42);

        Assert.That(staff.Model.DisplayedVoiceRoles.Select(r => r.Node?.Id), Is.EqualTo([2]));
    }

    [Test]
    public async Task ReturningToThePage_WithAdultContentUnchanged_DoesNotRefetch()
    {
        // The same-id short-circuit exists so a sort popup closing does not reset the sort the user
        // just picked. Nothing here may weaken it.
        AppSettings.DisplayAdultContent = false;
        var staff = StaffHarness.With(voiceRoles: [Role(1, Media(10))]);
        await staff.Model.LoadAsync(42);

        await staff.Model.LoadAsync(42);

        await staff.Client.Received(1).GetStaffAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── Builders ─────────────────────────────────────────────────────

    private static RelatedMedia Media(int id, bool adult = false)
        => new() { Id = id, Type = "ANIME", IsAdult = adult, Title = new MediaTitle { Romaji = $"Show {id}" } };

    private static StaffCharacterEdge Role(int characterId, params RelatedMedia[] media)
        => new()
        {
            Node = new Character { Id = characterId, Name = new CharacterName { Full = $"Character {characterId}" } },
            Role = "MAIN",
            AllMedia = media,
            Media = media.FirstOrDefault(),
        };

    private sealed class StaffHarness
    {
        private StaffHarness()
            => Model = new StaffDetailsPageModel(
                Client, Substitute.For<IAuthService>(), Substitute.For<INavigationService>(),
                new RecordingUserFeedback(), new RecordingExternalBrowser(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                NullLogger<StaffDetailsPageModel>.Instance);

        public StaffDetailsPageModel Model { get; }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public static StaffHarness With(
            StaffCharacterEdge[]? voiceRoles = null,
            StaffMediaEdge[]? productionRoles = null,
            bool moreVoiceRoles = false,
            bool moreProductionRoles = false)
        {
            var staff = new Staff
            {
                Id = 42,
                CharactersPageInfo = new PageInfo { CurrentPage = 1, HasNextPage = moreVoiceRoles },
                StaffMediaPageInfo = new PageInfo { CurrentPage = 1, HasNextPage = moreProductionRoles },
            };
            foreach (var role in voiceRoles ?? [])
            {
                staff.Characters.Add(role);
            }

            foreach (var role in productionRoles ?? [])
            {
                staff.StaffMedia.Add(role);
            }

            var harness = new StaffHarness();
            harness.Client
                .GetStaffAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Staff?>(staff));
            return harness;
        }
    }

    private sealed class CharacterHarness
    {
        public CharacterHarness()
            => Model = new CharacterDetailsPageModel(
                Client, Substitute.For<IAuthService>(), Substitute.For<INavigationService>(),
                new RecordingUserFeedback(), new RecordingExternalBrowser(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                NullLogger<CharacterDetailsPageModel>.Instance);

        public CharacterDetailsPageModel Model { get; }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public void Returns(Character character)
            => Client.GetCharacterAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Character?>(character));
    }

    private sealed class StudioHarness
    {
        public StudioHarness()
            => Model = new StudioDetailsPageModel(
                Client, Substitute.For<IAuthService>(), Substitute.For<INavigationService>(),
                new RecordingUserFeedback(), new RecordingExternalBrowser(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                NullLogger<StudioDetailsPageModel>.Instance);

        public StudioDetailsPageModel Model { get; }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public void Returns(Studio studio)
            => Client.GetStudioAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Studio?>(studio));
    }

    private sealed class MediaHarness
    {
        public MediaHarness()
        {
            var dialogs = new ScriptedDialogService();
            Model = new MediaDetailsPageModel(
                Client,
                Substitute.For<IAuthService>(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Substitute.For<INavigationService>(),
                new RecordingUserFeedback(),
                new RecordingExternalBrowser(),
                dialogs,
                new ListEntryStatusFlow(dialogs), Substitute.For<IUpNextOffer>(),
                NullLogger<MediaDetailsPageModel>.Instance);
        }

        public MediaDetailsPageModel Model { get; }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public void Returns(Media media)
            => Client.GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<(Media?, MediaListEntry?)>((media, null)));
    }
}
