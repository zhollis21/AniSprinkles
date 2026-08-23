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
    [Fact]
    public async Task LoadAsync_SeedsAppearancesAndVoiceActorsFromTheSameSingleFetch()
    {
        var harness = new Harness();
        harness.Returns(CharacterWith(
            Appearance(1, "Show A", Va(100, "Ann")),
            Appearance(2, "Show B", Va(101, "Bea"))));

        await harness.Model.LoadAsync(42);

        // Both sections come off the one heavy first-page query — the point of seeding them together.
        Assert.Equal(1, harness.Fetches);
        Assert.Equal(2, harness.Model.DisplayedAppearances.Count);
        Assert.Equal(2, harness.Model.VoiceActors.Count);
        Assert.True(harness.Model.HasAppearances);
        Assert.True(harness.Model.HasVoiceActors);
    }

    [Fact]
    public async Task LoadAsync_DedupesAVoiceActorAppearingAcrossSeveralAppearances()
    {
        var harness = new Harness();
        harness.Returns(CharacterWith(
            Appearance(1, "Show A", Va(100, "Ann")),
            Appearance(2, "Show B", Va(100, "Ann"))));

        await harness.Model.LoadAsync(42);

        Assert.Equal(2, harness.Model.DisplayedAppearances.Count);
        Assert.Single(harness.Model.VoiceActors);
    }

    [Fact]
    public async Task LoadAsync_ForACharacterWithNoMedia_StillReachesTheVoiceActorEmptyState()
    {
        var harness = new Harness();
        harness.Returns(new Character { Id = 42 });

        await harness.Model.LoadAsync(42);

        // Gated on the voice-actor section's own state, not on HasAppearances — a character with no
        // media at all should still get the friendly message rather than a blank section.
        Assert.True(harness.Model.ShowVoiceActorsEmptyState);
        Assert.True(harness.Model.ShowVoiceActorsSection);
        Assert.False(harness.Model.HasVoiceActors);
    }

    [Fact]
    public async Task BioStats_MaskSpoilerValuesUntilSpoilersAreShown()
    {
        var harness = new Harness();
        harness.Returns(new Character
        {
            Id = 42,
            Description = "__Height:__ 172 cm\n__Bounty:__ ~!3,000,000,000!~",
        });

        await harness.Model.LoadAsync(42);

        Assert.True(harness.Model.HasBioStats);
        Assert.True(harness.Model.HasSpoilers);
        Assert.Equal("172 cm", harness.Model.BioStats[0].ValueDisplay);
        Assert.True(harness.Model.BioStats[1].IsValueSpoilerHidden);
        Assert.DoesNotContain("3,000,000,000", harness.Model.BioStats[1].ValueDisplay, StringComparison.Ordinal);

        harness.Model.ToggleSpoilersCommand.Execute(null);

        Assert.False(harness.Model.BioStats[1].IsValueSpoilerHidden);
        Assert.Equal("3,000,000,000", harness.Model.BioStats[1].ValueDisplay);
    }

    [Fact]
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

        Assert.Equal(["Straw Hat"], harness.Model.AlternativeNames);
        Assert.True(harness.Model.HasSpoilers);

        harness.Model.ToggleSpoilersCommand.Execute(null);

        Assert.Equal(["Straw Hat", "Nika"], harness.Model.AlternativeNames);
    }

    [Fact]
    public async Task LoadAsync_ForANewCharacter_ClearsSpoilerAndExpandedStateFromThePreviousOne()
    {
        var harness = new Harness();
        harness.Returns(new Character { Id = 1, Description = "__A:__ ~!secret!~" });
        await harness.Model.LoadAsync(1);

        harness.Model.ToggleSpoilersCommand.Execute(null);
        harness.Model.ToggleDescriptionCommand.Execute(null);
        Assert.True(harness.Model.IsShowingSpoilers);
        Assert.True(harness.Model.IsDescriptionExpanded);

        harness.Returns(new Character { Id = 2 });
        await harness.Model.LoadAsync(2);

        // Carrying a revealed spoiler across to a different character would leak the previous one's.
        Assert.False(harness.Model.IsShowingSpoilers);
        Assert.False(harness.Model.IsDescriptionExpanded);
    }

    [Fact]
    public async Task NavigateToStaff_NavigatesWithTheStaffId()
    {
        var harness = new Harness();

        await harness.Model.NavigateToStaffCommand.ExecuteAsync(7);

        await harness.Navigation.Received(1).GoToAsync(
            "staff-details",
            false,
            Arg.Is<IDictionary<string, object>>(d => (int)d["staffId"] == 7));
    }

    [Fact]
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
