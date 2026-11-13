using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// What the character page does beyond the shared spine (see <see cref="DetailsSpineTests{TEntity}"/>):
/// two independent views over the same media page, and the spoiler-gated bio surface.
/// </summary>
public class CharacterDetailsPageModelTests
{
    [Test]
    public async Task LoadAsync_SeedsAppearancesAndVoiceActorsFromTheSameSingleFetch()
    {
        var harness = new Harness();
        harness.Returns(CharacterWith(
            Appearance(1, "Show A", Va(100, "Ann")),
            Appearance(2, "Show B", Va(101, "Bea"))));

        await harness.Model.LoadAsync(42);

        // Both sections come off the one heavy first-page query — the point of seeding them together.
        Assert.That(harness.Fetches, Is.EqualTo(1));
        Assert.That(harness.Model.DisplayedAppearances.Count, Is.EqualTo(2));
        Assert.That(harness.Model.VoiceActors.Count, Is.EqualTo(2));
        Assert.That(harness.Model.HasAppearances, Is.True);
        Assert.That(harness.Model.HasVoiceActors, Is.True);
    }

    [Test]
    public async Task LoadAsync_DedupesAVoiceActorAppearingAcrossSeveralAppearances()
    {
        var harness = new Harness();
        harness.Returns(CharacterWith(
            Appearance(1, "Show A", Va(100, "Ann")),
            Appearance(2, "Show B", Va(100, "Ann"))));

        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.DisplayedAppearances.Count, Is.EqualTo(2));
        Assert.That(harness.Model.VoiceActors, Has.Exactly(1).Items);
    }

    [Test]
    public async Task LoadAsync_ForACharacterWithNoMedia_StillReachesTheVoiceActorEmptyState()
    {
        var harness = new Harness();
        harness.Returns(new Character { Id = 42 });

        await harness.Model.LoadAsync(42);

        // Gated on the voice-actor section's own state, not on HasAppearances — a character with no
        // media at all should still get the friendly message rather than a blank section.
        Assert.That(harness.Model.ShowVoiceActorsEmptyState, Is.True);
        Assert.That(harness.Model.ShowVoiceActorsSection, Is.True);
        Assert.That(harness.Model.HasVoiceActors, Is.False);
    }

    [Test]
    public async Task BioStats_MaskSpoilerValuesUntilSpoilersAreShown()
    {
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "__Height:__ 172 cm\n__Bounty:__ ~!3,000,000,000!~",
        });

        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.HasBioStats, Is.True);
        Assert.That(harness.Model.HasSpoilers, Is.True);
        Assert.That(harness.Model.BioStats[0].ValueDisplay, Is.EqualTo("172 cm"));
        Assert.That(harness.Model.BioStats[1].IsValueSpoilerHidden, Is.True);
        Assert.That(harness.Model.BioStats[1].ValueDisplay, Does.Not.Contain("3,000,000,000"));

        harness.Model.ToggleSpoilersCommand.Execute(null);

        Assert.That(harness.Model.BioStats[1].IsValueSpoilerHidden, Is.False);
        Assert.That(harness.Model.BioStats[1].ValueDisplay, Is.EqualTo("3,000,000,000"));
    }

    [Test]
    public async Task AlternativeNames_IncludeSpoilerNamesOnlyWhileSpoilersAreShown()
    {
        var harness = new Harness();
        var character = new Character
        {
            Id = 42,
            Name = new CharacterName { Full = "Luffy" },
        };
        character.Name.Alternative.Add("Straw Hat");
        character.Name.AlternativeSpoiler.Add("Nika");
        harness.Returns(character);

        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.AlternativeNames, Is.EqualTo(["Straw Hat"]));
        Assert.That(harness.Model.HasSpoilers, Is.True);

        harness.Model.ToggleSpoilersCommand.Execute(null);

        Assert.That(harness.Model.AlternativeNames, Is.EqualTo(["Straw Hat", "Nika"]));
    }

    [Test]
    public async Task LoadAsync_ForANewCharacter_ClearsSpoilerAndExpandedStateFromThePreviousOne()
    {
        var harness = new Harness();
        harness.Returns(new Character { Id = 1, Description = "__A:__ ~!secret!~" });
        await harness.Model.LoadAsync(1);

        harness.Model.ToggleSpoilersCommand.Execute(null);
        harness.Model.ToggleDescriptionCommand.Execute(null);
        Assert.That(harness.Model.IsShowingSpoilers, Is.True);
        Assert.That(harness.Model.IsDescriptionExpanded, Is.True);

        harness.Returns(new Character { Id = 2 });
        await harness.Model.LoadAsync(2);

        // Carrying a revealed spoiler across to a different character would leak the previous one's.
        Assert.That(harness.Model.IsShowingSpoilers, Is.False);
        Assert.That(harness.Model.IsDescriptionExpanded, Is.False);
    }

    // ── Spoilers meeting the paragraph markup (#138 × the spoiler pass) ──────────────
    //
    // BioProse runs SpoilerHtmlProcessor over the *output* of AniListMarkdownProcessor, so since
    // #138 the spoiler pass sees <br> markup inside the regions it has to mask. This is not an edge
    // case: of the 150 most-favourited characters and 50 staff, 65 carry spoilers in the prose and
    // 13 of those wrap a line break. Luffy — the character both issues were filed from — is in the
    // 7 whose spoilers sit only in stat rows, which is why the device pass never exercised this.

    [Test]
    public async Task ASpoilerSpanningParagraphs_CollapsesToOneChipWhenHidden()
    {
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "Before.\n\n~!Secret one.\n\nSecret two.!~\n\nAfter.",
        });

        await harness.Model.LoadAsync(42);

        var prose = harness.Model.BioProse;

        // The whole region, breaks included, becomes a single chip — no fragments of the hidden
        // text and no stray <br> escaping from inside it.
        Assert.That(prose, Does.Not.Contain("Secret"));
        Assert.That(CountOccurrences(prose, "[spoiler]"), Is.EqualTo(1));
        Assert.That(prose, Does.StartWith("Before.<br><br>"));
        Assert.That(prose, Does.EndWith("<br><br>After."));
    }

    [Test]
    public async Task ASpoilerSpanningParagraphs_KeepsItsBreaksWhenRevealed()
    {
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "Before.\n\n~!Secret one.\n\nSecret two.!~",
        });
        await harness.Model.LoadAsync(42);

        harness.Model.ToggleSpoilersCommand.Execute(null);

        // Revealing has to give back the paragraph structure, not a run-on.
        Assert.That(
            harness.Model.BioProse,
            Is.EqualTo("Before.<br><br>Secret one.<br><br>Secret two."));
    }

    [Test]
    public async Task TwoSpoilersSeparatedByAParagraphBreak_StayTwoChips()
    {
        // Character 126824's shape: one spoiler ends, a blank line, another begins. The spoiler
        // pattern is non-greedy, so the two must not coalesce into one chip that swallows the
        // visible break between them.
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "~!First secret.!~\n\n~!Second secret.!~",
        });

        await harness.Model.LoadAsync(42);

        Assert.That(CountOccurrences(harness.Model.BioProse, "[spoiler]"), Is.EqualTo(2));
        Assert.That(harness.Model.BioProse, Does.Contain("<br><br>"));
    }

    [Test]
    public async Task ALinkInsideASpoiler_SurvivesRevealAndStaysHiddenOtherwise()
    {
        // Characters 35252 and 17 both link other characters from inside a spoiler, so the anchor
        // the Android span code looks for has to come through the spoiler pass intact.
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "~!He confesses to [Makise Kurisu](https://anilist.co/character/2)!~",
        });
        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.BioProse, Does.Not.Contain("anilist.co"));

        harness.Model.ToggleSpoilersCommand.Execute(null);

        Assert.That(
            harness.Model.BioProse,
            Is.EqualTo("He confesses to <a href=\"https://anilist.co/character/2\">Makise Kurisu</a>"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Test]
    public async Task BioStats_RenderMarkdownInTheValue_RatherThanLeakingItVerbatim()
    {
        // AniList puts links in stat rows as well as prose — staff 96881's "Favorite Mangaka" is a
        // link — and the stat path never ran the markdown processor, so the row showed a literal
        // [name](url) next to prose that rendered correctly.
        var harness = new Harness();
        harness.Returns(new Character
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
    public async Task BioStats_LeaveTheSpoilerMaskAlone()
    {
        // The mask is a run of block characters, not markdown — processing it would be pointless at
        // best, and it must keep hiding the value.
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "__Bounty:__ ~![Berry](https://anilist.co/staff/1) 3,000,000,000!~",
        });

        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.BioStats[0].IsValueSpoilerHidden, Is.True);
        Assert.That(harness.Model.BioStats[0].ValueDisplay, Does.Not.Contain("anilist.co"));
        Assert.That(harness.Model.BioStats[0].ValueDisplay, Does.Not.Contain("Berry"));
    }

    [Test]
    public async Task IsDescriptionTruncated_MeasuresTheRenderedBio_NotTheRawMarkdown()
    {
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "One.\n\nTwo.\n\nThree.\n\nFour.",
        });

        await harness.Model.LoadAsync(42);

        // Comfortably under the character count, but it renders as seven visual lines once the
        // paragraph breaks become <br>. Measuring the raw markdown instead — which contains no
        // <br> by construction — this reads as text that fits, so no "Read more" appears while the
        // label clamps and tail-truncates anyway. That is the direction #138 rules out.
        Assert.That(harness.Model.IsDescriptionTruncated, Is.True);
    }

    [Test]
    public async Task TogglingSpoilers_RenotifiesTruncation_BecauseRevealingChangesHowMuchThereIsToShow()
    {
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "Prose.\n\n~!" + new string('a', 400) + "!~",
        });
        await harness.Model.LoadAsync(42);

        // Hidden, the spoiler collapses to a short "[spoiler]" chip and the bio fits.
        Assert.That(harness.Model.IsDescriptionTruncated, Is.False);

        var raised = new List<string?>();
        harness.Model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        harness.Model.ToggleSpoilersCommand.Execute(null);

        Assert.That(raised, Does.Contain(nameof(CharacterDetailsPageModel.IsDescriptionTruncated)));
        Assert.That(harness.Model.IsDescriptionTruncated, Is.True);
    }

    [Test]
    public async Task NavigateToStaff_NavigatesWithTheStaffId()
    {
        var harness = new Harness();

        await harness.Model.NavigateToStaffCommand.ExecuteAsync(7);

        await harness.Navigation.Received(1).GoToAsync(
            "staff-details",
            false,
            Arg.Is<IDictionary<string, object>>(d => (int)d["staffId"] == 7));
    }

    [Test]
    public async Task NavigateToStaff_WithAnInvalidId_DoesNothing()
    {
        var harness = new Harness();

        await harness.Model.NavigateToStaffCommand.ExecuteAsync(0);

        await harness.Navigation.DidNotReceive().GoToAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>());
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static VoiceActor Va(int id, string name)
        => new() { Id = id, Name = new CharacterName { Full = name }, Language = "Japanese" };

    private static CharacterMediaEdge Appearance(int mediaId, string title, params VoiceActor[] voiceActors)
        => new()
        {
            Node = new RelatedMedia { Id = mediaId, Type = "ANIME", Title = new MediaTitle { Romaji = title } },
            CharacterRole = "MAIN",
            VoiceActors = [.. voiceActors],
        };

    private static Character CharacterWith(params CharacterMediaEdge[] appearances)
    {
        var character = new Character { Id = 42 };
        foreach (var edge in appearances)
        {
            character.Media.Add(edge);
        }

        return character;
    }

    private sealed class Harness
    {
        public Harness()
            => Model = new CharacterDetailsPageModel(
                Client, Auth, Navigation, Feedback, Browser,
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                NullLogger<CharacterDetailsPageModel>.Instance);

        public CharacterDetailsPageModel Model { get; }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();

        public INavigationService Navigation { get; } = Substitute.For<INavigationService>();

        public RecordingUserFeedback Feedback { get; } = new();

        public RecordingExternalBrowser Browser { get; } = new();

        public int Fetches { get; private set; }

        public void Returns(Character? character)
            => Client.GetCharacterAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    Fetches++;
                    return Task.FromResult(character);
                });
    }
}
