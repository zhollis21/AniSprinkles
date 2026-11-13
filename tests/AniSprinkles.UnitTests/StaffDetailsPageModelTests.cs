using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// What the staff page does beyond the shared spine (see <see cref="DetailsSpineTests{TEntity}"/>):
/// two genuinely separate AniList connections, each with its own cursor and sort.
/// </summary>
public class StaffDetailsPageModelTests
{
    [Test]
    public async Task LoadAsync_SeedsVoiceRolesAndProductionRolesFromTheirOwnConnections()
    {
        var harness = new Harness();
        harness.Returns(StaffWith(voiceRoles: 2, productionRoles: 3));

        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.DisplayedVoiceRoles.Count, Is.EqualTo(2));
        Assert.That(harness.Model.DisplayedProductionRoles.Count, Is.EqualTo(3));
        Assert.That(harness.Model.HasVoiceRoles, Is.True);
        Assert.That(harness.Model.HasProductionRoles, Is.True);
    }

    [Test]
    public async Task LoadAsync_UsesEachSectionsOwnDefaultSort()
    {
        var harness = new Harness();
        harness.Returns(StaffWith(voiceRoles: 1, productionRoles: 1));

        await harness.Model.LoadAsync(42);

        // The defaults must match the sub-block sorts of the heavy Staff query, or the seeded page and
        // the highlighted dropdown option disagree.
        Assert.That(harness.Model.VoiceRolesSort, Is.EqualTo("FAVOURITES_DESC"));
        Assert.That(harness.Model.ProductionRolesSort, Is.EqualTo("POPULARITY_DESC"));
        Assert.That(harness.Model.VoiceRolesSortOptions.Single(o => o.Code == "FAVOURITES_DESC").IsSelected, Is.True);
        Assert.That(harness.Model.ProductionRolesSortOptions.Single(o => o.Code == "POPULARITY_DESC").IsSelected, Is.True);
    }

    [Test]
    public async Task SelectVoiceRolesSort_RefetchesVoiceRolesAndLeavesProductionRolesAlone()
    {
        var harness = new Harness();
        harness.Returns(StaffWith(voiceRoles: 2, productionRoles: 3, moreVoiceRoles: true));
        await harness.Model.LoadAsync(42);

        harness.Client
            .LoadStaffCharactersPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StaffCharacterEdge>, PageInfo?)>(
                ([VoiceRole(99)], new PageInfo { CurrentPage = 1, HasNextPage = false })));

        await harness.Model.SelectVoiceRolesSortCommand.ExecuteAsync("ROLE");

        Assert.That(harness.Model.VoiceRolesSort, Is.EqualTo("ROLE"));
        Assert.That(harness.Model.DisplayedVoiceRoles, Has.Exactly(1).Items);
        Assert.That(harness.Model.VoiceRolesSortOptions.Single(o => o.Code == "ROLE").IsSelected, Is.True);

        // The two connections are independent: re-sorting one must not disturb the other.
        Assert.That(harness.Model.DisplayedProductionRoles.Count, Is.EqualTo(3));
        Assert.That(harness.Model.ProductionRolesSort, Is.EqualTo("POPULARITY_DESC"));
        await harness.Client.DidNotReceive().LoadStaffMediaPageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SelectVoiceRolesSort_WithTheCompleteList_ReordersInMemoryWithoutAnApiCall()
    {
        var harness = new Harness();
        harness.Returns(StaffWith(voiceRoles: 2, productionRoles: 1));
        await harness.Model.LoadAsync(42);

        await harness.Model.SelectVoiceRolesSortCommand.ExecuteAsync("ROLE");

        // Once the whole connection is loaded the server can't know anything we don't — spending a
        // rate-limited request to re-sort it would be waste.
        Assert.That(harness.Model.VoiceRolesSort, Is.EqualTo("ROLE"));
        Assert.That(harness.Model.DisplayedVoiceRoles.Count, Is.EqualTo(2));
        await harness.Client.DidNotReceive().LoadStaffCharactersPageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SelectVoiceRolesSort_WithTheSortAlreadyActive_DoesNotRefetch()
    {
        var harness = new Harness();
        harness.Returns(StaffWith(voiceRoles: 2, productionRoles: 1));
        await harness.Model.LoadAsync(42);

        await harness.Model.SelectVoiceRolesSortCommand.ExecuteAsync("FAVOURITES_DESC");

        await harness.Client.DidNotReceive().LoadStaffCharactersPageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAsync_ForANewStaffMember_ResetsBothSortSelections()
    {
        var harness = new Harness();
        harness.Returns(StaffWith(voiceRoles: 1, productionRoles: 1));
        await harness.Model.LoadAsync(1);

        harness.Client
            .LoadStaffCharactersPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StaffCharacterEdge>, PageInfo?)>(([], null)));
        await harness.Model.SelectVoiceRolesSortCommand.ExecuteAsync("ROLE");
        Assert.That(harness.Model.VoiceRolesSort, Is.EqualTo("ROLE"));

        harness.Returns(StaffWith(voiceRoles: 1, productionRoles: 1));
        await harness.Model.LoadAsync(2);

        Assert.That(harness.Model.VoiceRolesSort, Is.EqualTo("FAVOURITES_DESC"));
        Assert.That(harness.Model.VoiceRolesSortOptions.Single(o => o.Code == "FAVOURITES_DESC").IsSelected, Is.True);
    }

    [Test]
    public async Task NavigateToCharacter_NavigatesWithTheCharacterId()
    {
        var harness = new Harness();

        await harness.Model.NavigateToCharacterCommand.ExecuteAsync(7);

        await harness.Navigation.Received(1).GoToAsync(
            "character-details",
            false,
            Arg.Is<IDictionary<string, object>>(d => (int)d["characterId"] == 7));
    }

    [Test]
    public async Task NavigateToCharacter_WithAnInvalidId_DoesNothing()
    {
        var harness = new Harness();

        await harness.Model.NavigateToCharacterCommand.ExecuteAsync(0);

        await harness.Navigation.DidNotReceive().GoToAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>());
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static StaffCharacterEdge VoiceRole(int characterId)
        => new()
        {
            Node = new Character { Id = characterId, Name = new CharacterName { Full = $"Character {characterId}" } },
            Role = "MAIN",
            Media = new RelatedMedia { Id = characterId * 10, Type = "ANIME" },
        };

    private static StaffMediaEdge ProductionRole(int mediaId)
        => new()
        {
            Node = new RelatedMedia { Id = mediaId, Type = "ANIME", Title = new MediaTitle { Romaji = $"Show {mediaId}" } },
            StaffRole = "Director",
        };

    /// <param name="moreVoiceRoles">Leaves the voice-roles cursor mid-list, so a sort change has to go
    /// back to the server rather than reordering the complete set in memory.</param>
    private static Staff StaffWith(int voiceRoles, int productionRoles, bool moreVoiceRoles = false)
    {
        var staff = new Staff { Id = 42 };
        for (var i = 1; i <= voiceRoles; i++)
        {
            staff.Characters.Add(VoiceRole(i));
        }

        for (var i = 1; i <= productionRoles; i++)
        {
            staff.StaffMedia.Add(ProductionRole(i));
        }

        staff.CharactersPageInfo = new PageInfo { CurrentPage = 1, HasNextPage = moreVoiceRoles };
        return staff;
    }

    // ── The bio surface (#137, #138) ─────────────────────────────────
    //
    // StaffDetailsPageModel carries the same bio pipeline as the character page — BioProse,
    // BioStats and the truncation gate are duplicated on both — and both were changed together.
    // Staff bios are the ones that exercise it hardest: they are where the Markdown lists and the
    // links to agencies and other creators live.

    [Test]
    public async Task BioProse_TurnsNewlinesIntoBreaks_SoAListKeepsItsShape()
    {
        // Staff 96881's __Trivia:__ list in miniature. Lone newlines are structural here, so
        // collapsing them to spaces would run every bullet onto one line.
        var harness = new Harness();
        harness.Returns(new Staff { Id = 42, Description = "__Trivia:__\n- Married.\n- Loves Lupin III." });

        await harness.Model.LoadAsync(42);

        Assert.That(
            harness.Model.BioProse,
            Is.EqualTo("<b>Trivia:</b><br>- Married.<br>- Loves Lupin III."));
    }

    [Test]
    public async Task IsDescriptionTruncated_MeasuresTheRenderedBio_NotTheRawMarkdown()
    {
        var harness = new Harness();
        harness.Returns(new Staff { Id = 42, Description = "One.\n\nTwo.\n\nThree.\n\nFour." });

        await harness.Model.LoadAsync(42);

        // Short on characters, but seven visual lines once the breaks become <br>. Measured against
        // the raw markdown — which contains no <br> by construction — this reads as text that fits,
        // so no "Read more" appears while the label clamps and tail-truncates anyway.
        Assert.That(harness.Model.IsDescriptionTruncated, Is.True);
    }

    [Test]
    public async Task TogglingSpoilers_RenotifiesTruncation()
    {
        var harness = new Harness();
        harness.Returns(new Staff
        {
            Id = 42,
            Description = "Prose.\n\n~!" + new string('a', 400) + "!~",
        });
        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.IsDescriptionTruncated, Is.False);

        var raised = new List<string?>();
        harness.Model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        harness.Model.ToggleSpoilersCommand.Execute(null);

        Assert.That(raised, Does.Contain(nameof(StaffDetailsPageModel.IsDescriptionTruncated)));
        Assert.That(harness.Model.IsDescriptionTruncated, Is.True);
    }

    [Test]
    public async Task BioStats_RenderMarkdownInTheValue()
    {
        // Staff 96881's "Favorite Mangaka" is a link, and the stat path never ran the markdown
        // processor — so the row showed a literal [name](url) beside prose that rendered correctly.
        var harness = new Harness();
        harness.Returns(new Staff
        {
            Id = 42,
            Description = "__Favorite Mangaka:__ [Akira Toriyama](https://anilist.co/staff/96901)",
        });

        await harness.Model.LoadAsync(42);

        Assert.That(
            harness.Model.BioStats[0].ValueDisplay,
            Is.EqualTo("<a href=\"https://anilist.co/staff/96901\">Akira Toriyama</a>"));
    }

    [Test]
    public async Task BioStats_AreStillFoundWhenTheColonSitsOutsideTheUnderscores()
    {
        // A miss on the opening line latches the parser into prose, so this spelling used to cost
        // the whole stat card — later rows in the accepted spelling included.
        var harness = new Harness();
        harness.Returns(new Staff { Id = 42, Description = "__Height__: 172 cm\n__Born:__ Kumamoto\n\nProse." });

        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.BioStats.Count, Is.EqualTo(2));
        Assert.That(harness.Model.BioStats[0].LabelDisplay, Is.EqualTo("Height"));
        Assert.That(harness.Model.BioStats[1].LabelDisplay, Is.EqualTo("Born"));
        Assert.That(harness.Model.BioProse, Is.EqualTo("Prose."));
    }

    [Test]
    public async Task ASpoilerSpanningParagraphs_CollapsesToOneChipThenRestoresItsBreaks()
    {
        var harness = new Harness();
        harness.Returns(new Staff { Id = 42, Description = "Before.\n\n~!Secret one.\n\nSecret two.!~" });
        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.BioProse, Does.Not.Contain("Secret"));
        Assert.That(harness.Model.BioProse, Does.Contain("[spoiler]"));

        harness.Model.ToggleSpoilersCommand.Execute(null);

        Assert.That(
            harness.Model.BioProse,
            Is.EqualTo("Before.<br><br>Secret one.<br><br>Secret two."));
    }

    private sealed class Harness
    {
        public Harness()
            => Model = new StaffDetailsPageModel(
                Client, Auth, Navigation, Feedback, Browser,
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                NullLogger<StaffDetailsPageModel>.Instance);

        public StaffDetailsPageModel Model { get; }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();

        public INavigationService Navigation { get; } = Substitute.For<INavigationService>();

        public RecordingUserFeedback Feedback { get; } = new();

        public RecordingExternalBrowser Browser { get; } = new();

        public void Returns(Staff? staff)
            => Client.GetStaffAsync(
                    Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(staff));
    }
}
