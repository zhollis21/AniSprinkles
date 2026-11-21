using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #103: offering a completed entry's sequels, side stories, spin-offs and alternatives. The rules
/// decide what gets saved to the user's real list, and a wrong one either nags (offers a recap film
/// or something already watched) or overwrites (re-adds a title that is already on the list), so
/// each is pinned here rather than left to a device run.
/// </summary>
[NonParallelizable]
public class UpNextTests
{
    public UpNextTests() => TestDataBuilder.ResetAppSettings();

    // ── Which relations are offered ──────────────────────────────────

    [Test]
    public void Candidates_OnlyTheForwardRelationTypes()
    {
        var edges = new[]
        {
            Edge(MediaRelationKind.Prequel, 1),
            Edge(MediaRelationKind.Sequel, 2),
            Edge(MediaRelationKind.SideStory, 3),
            Edge(MediaRelationKind.SpinOff, 4),
            Edge(MediaRelationKind.Alternative, 5),
            Edge(MediaRelationKind.Summary, 6),
            Edge(MediaRelationKind.Compilation, 7),
            Edge(MediaRelationKind.Parent, 8),
            Edge(MediaRelationKind.Source, 9),
            Edge(MediaRelationKind.Adaptation, 10),
            Edge(MediaRelationKind.Character, 11),
            Edge(MediaRelationKind.Other, 12),
            Edge(MediaRelationKind.Contains, 13),
            Edge(MediaRelationKind.SameUniverse, 14),
            Edge(MediaRelationKind.Unknown, 15),
        };

        var ids = Pick(edges).Select(c => c.Media.Id);

        Assert.That(ids, Is.EquivalentTo([2, 3, 4, 5]));
    }

    [Test]
    public void Candidates_SameMediaTypeOnly()
    {
        // An anime's "sequel" edge can point at a manga; a manga completion can have an anime one.
        var edges = new[]
        {
            Edge(MediaRelationKind.Sequel, 1, type: "ANIME"),
            Edge(MediaRelationKind.Sequel, 2, type: "MANGA"),
        };

        Assert.That(Pick(edges).Select(c => c.Media.Id), Is.EqualTo([1]));
        Assert.That(Pick(edges, kind: MediaKind.Manga).Select(c => c.Media.Id), Is.EqualTo([2]));
    }

    [Test]
    public void Candidates_SkipAnythingOnTheListWithoutItsWholeEntry()
    {
        // The details query's four-field snapshot: moving from it would save score, notes and repeat
        // count back as nulls. Only the relations query's whole entry is safe to move.
        var listed = Edge(MediaRelationKind.Sequel, 1, configure: n => { n.ListEntryId = 77; n.ListStatus = MediaListStatus.Planning; });
        var idOnly = Edge(MediaRelationKind.Sequel, 2, configure: n => n.ListEntryId = 78);

        var candidates = Pick([listed, idOnly, Edge(MediaRelationKind.Sequel, 3)]);

        Assert.That(candidates.Select(c => c.Media.Id), Is.EqualTo([3]));
    }

    // The matrix agreed for #103: where a related title sits on the list, whether it has aired, and
    // the Settings list decide whether it is offered and where it goes. Finishing a first watch and
    // finishing a rewatch are treated alike. Null onList is "not on the list"; null expected is "not
    // offered".
    [TestCase(null, "FINISHED", MediaListStatus.Current, MediaListStatus.Current)]
    [TestCase(null, "FINISHED", MediaListStatus.Planning, MediaListStatus.Planning)]
    [TestCase(null, "RELEASING", MediaListStatus.Current, MediaListStatus.Current)]
    [TestCase(null, "RELEASING", MediaListStatus.Planning, MediaListStatus.Planning)]
    [TestCase(null, "NOT_YET_RELEASED", MediaListStatus.Current, MediaListStatus.Planning)]
    [TestCase(null, "NOT_YET_RELEASED", MediaListStatus.Planning, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Planning, "FINISHED", MediaListStatus.Current, MediaListStatus.Current)]
    [TestCase(MediaListStatus.Planning, "FINISHED", MediaListStatus.Planning, null)]
    [TestCase(MediaListStatus.Planning, "RELEASING", MediaListStatus.Current, MediaListStatus.Current)]
    [TestCase(MediaListStatus.Planning, "NOT_YET_RELEASED", MediaListStatus.Current, null)]
    [TestCase(MediaListStatus.Planning, "NOT_YET_RELEASED", MediaListStatus.Planning, null)]
    // An unreleased title only ever goes to Planning, wherever it sits (agreed 2026-11-20).
    [TestCase(MediaListStatus.Paused, "NOT_YET_RELEASED", MediaListStatus.Current, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Paused, "NOT_YET_RELEASED", MediaListStatus.Planning, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Dropped, "NOT_YET_RELEASED", MediaListStatus.Current, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Completed, "NOT_YET_RELEASED", MediaListStatus.Current, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Completed, "NOT_YET_RELEASED", MediaListStatus.Planning, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Current, "NOT_YET_RELEASED", MediaListStatus.Current, null)]
    // A cancelled title is never offered: one that never aired has nothing to watch (agreed 2026-11-20).
    [TestCase(null, "CANCELLED", MediaListStatus.Current, null)]
    [TestCase(null, "CANCELLED", MediaListStatus.Planning, null)]
    [TestCase(MediaListStatus.Planning, "CANCELLED", MediaListStatus.Current, null)]
    [TestCase(MediaListStatus.Paused, "CANCELLED", MediaListStatus.Current, null)]
    [TestCase(MediaListStatus.Completed, "CANCELLED", MediaListStatus.Current, null)]
    // Hiatus has aired, so it is released.
    [TestCase(null, "HIATUS", MediaListStatus.Current, MediaListStatus.Current)]
    [TestCase(MediaListStatus.Paused, "FINISHED", MediaListStatus.Current, MediaListStatus.Current)]
    [TestCase(MediaListStatus.Paused, "FINISHED", MediaListStatus.Planning, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Dropped, "FINISHED", MediaListStatus.Current, MediaListStatus.Current)]
    [TestCase(MediaListStatus.Dropped, "FINISHED", MediaListStatus.Planning, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Completed, "FINISHED", MediaListStatus.Current, MediaListStatus.Repeating)]
    [TestCase(MediaListStatus.Completed, "FINISHED", MediaListStatus.Planning, MediaListStatus.Planning)]
    [TestCase(MediaListStatus.Current, "RELEASING", MediaListStatus.Current, null)]
    [TestCase(MediaListStatus.Current, "RELEASING", MediaListStatus.Planning, null)]
    [TestCase(MediaListStatus.Repeating, "FINISHED", MediaListStatus.Current, null)]
    [TestCase(MediaListStatus.Repeating, "FINISHED", MediaListStatus.Planning, null)]
    public void Candidates_FollowTheMatrix(
        MediaListStatus? onList, string airing, MediaListStatus setting, MediaListStatus? expected)
    {
        var edge = Edge(MediaRelationKind.Sequel, 1, status: airing,
            configure: n => n.ViewerEntry = onList is { } status ? OnList(1, status) : null);

        var candidates = Pick([edge], setting: setting);

        Assert.That(candidates.Select(c => (MediaListStatus?)c.Target), Is.EqualTo(expected is { } target ? [target] : Array.Empty<MediaListStatus?>()));
        if (candidates is [var only])
        {
            Assert.That(only.IsNew, Is.EqualTo(onList is null));
            Assert.That(only.From, Is.EqualTo(onList));
        }
    }

    [Test]
    public void Candidates_AnEntryWithNoStatus_IsNotOffered()
    {
        var edge = Edge(MediaRelationKind.Sequel, 1, configure: n => n.ViewerEntry = new MediaListEntry { Id = 9, MediaId = 1 });

        Assert.That(Pick([edge]), Is.Empty);
    }

    [TestCase(MediaListStatus.Completed)]
    [TestCase(MediaListStatus.Dropped)]
    [TestCase(MediaListStatus.Paused)]
    [TestCase(MediaListStatus.Repeating)]
    [TestCase((MediaListStatus)999)]
    public void Candidates_RejectASettingThatIsNotWatchingOrPlanning(MediaListStatus setting)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Pick([Edge(MediaRelationKind.Sequel, 1)], setting: setting));

    [Test]
    public void Candidate_EachRowSaysWhereItGoes_InTheTypesOwnWords()
    {
        // No sheet-wide list switch: rows can go to different lists, so each names its own.
        Assert.That(One(Edge(MediaRelationKind.Sequel, 1)).DestinationLabel, Is.EqualTo("→ Watching"));
        Assert.That(One(Edge(MediaRelationKind.Sequel, 1), MediaListStatus.Planning).DestinationLabel, Is.EqualTo("→ Plan to Watch"));
        Assert.That(One(Edge(MediaRelationKind.Sequel, 1, status: "NOT_YET_RELEASED")).DestinationLabel, Is.EqualTo("→ Plan to Watch"));
        Assert.That(One(Planned(1)).DestinationLabel, Is.EqualTo("Plan to Watch → Watching"));
        Assert.That(One(Edge(MediaRelationKind.SpinOff, 1, type: "MANGA"), kind: MediaKind.Manga).DestinationLabel, Is.EqualTo("→ Reading"));

        var completedManga = Edge(MediaRelationKind.Sequel, 2, type: "MANGA", configure: n => n.ViewerEntry = OnList(2, MediaListStatus.Completed));
        Assert.That(One(completedManga, kind: MediaKind.Manga).DestinationLabel, Is.EqualTo("Completed → Rereading"));
        Assert.That(One(completedManga, MediaListStatus.Planning, MediaKind.Manga).DestinationLabel, Is.EqualTo("Completed → Plan to Read"));
    }

    [Test]
    public void Candidate_KeepsWhereItCameFrom_AfterItsEntryIsMoved()
    {
        // The move writes the new status into the candidate's own entry; the row and toast must not
        // follow it (a toast once said "moved to Watching" for a rewatch, #103).
        var candidate = One(Edge(MediaRelationKind.Sequel, 1, configure: n => n.ViewerEntry = OnList(1, MediaListStatus.Completed)));
        candidate.Existing?.Status = MediaListStatus.Repeating;

        Assert.That(candidate.From, Is.EqualTo(MediaListStatus.Completed));
        Assert.That(candidate.DestinationLabel, Is.EqualTo("Completed → Rewatching"));
    }

    [Test]
    public void Candidates_FollowTheAdultSetting()
    {
        var adult = Edge(MediaRelationKind.Sequel, 1, configure: n => n.IsAdult = true);

        Assert.That(Pick([adult], adult: false), Is.Empty);
        Assert.That(Pick([adult], adult: true), Has.Count.EqualTo(1));
    }

    [Test]
    public void Candidates_SequelsFirstThenInReleaseOrder_WithoutDuplicates()
    {
        var edges = new[]
        {
            Edge(MediaRelationKind.SideStory, 1, year: 2020),
            Edge(MediaRelationKind.Sequel, 2, year: 2024),
            Edge(MediaRelationKind.Sequel, 3, year: 2022),
            Edge(MediaRelationKind.Sequel, 4, year: null),
            Edge(MediaRelationKind.Sequel, 3, year: 2022),
        };

        Assert.That(Pick(edges).Select(c => c.Media.Id), Is.EqualTo([3, 2, 4, 1]));
    }

    [Test]
    public void Candidate_DetailsLineOmitsWhatIsMissing()
    {
        var full = Edge(MediaRelationKind.Sequel, 1, status: "FINISHED", year: 2026, configure: n => n.Format = "TV");

        Assert.That(One(full).DetailsLine, Is.EqualTo("TV · 2026"));
        Assert.That(One(Edge(MediaRelationKind.SpinOff, 2, year: null)).DetailsLine, Is.Empty);
    }

    [Test]
    public void ConfirmLabel_SummarisesTheSelection_AndSaysWhatToDoWhenEmpty()
    {
        var add = One(Edge(MediaRelationKind.Sequel, 1));
        var move = One(Planned(2));

        Assert.That(UpNextCandidates.ConfirmLabel([]), Is.EqualTo("Select titles"));
        Assert.That(UpNextCandidates.ConfirmLabel([add, add]), Is.EqualTo("Add 2"));
        Assert.That(UpNextCandidates.ConfirmLabel([move]), Is.EqualTo("Move 1"));
        Assert.That(UpNextCandidates.ConfirmLabel([add, move]), Is.EqualTo("Add 1 · Move 1"));
    }

    [TestCase("SIDE_STORY", MediaRelationKind.SideStory)]
    [TestCase("SPIN_OFF", MediaRelationKind.SpinOff)]
    [TestCase("SEQUEL", MediaRelationKind.Sequel)]
    [TestCase("SAME_UNIVERSE", MediaRelationKind.SameUniverse)]
    [TestCase("SOMETHING_NEW", MediaRelationKind.Unknown)]
    [TestCase("3", MediaRelationKind.Unknown)]
    [TestCase(null, MediaRelationKind.Unknown)]
    public void RelationKind_ParsesAniListsEnum(string? raw, MediaRelationKind expected)
        => Assert.That(MediaRelationKindExtensions.ParseMediaRelationKind(raw), Is.EqualTo(expected));

    // ── Preferences ──────────────────────────────────────────────────

    [Test]
    public void Preferences_DefaultToOnAndWatching()
    {
        var preferences = new FakePreferences();

        Assert.That(UpNextPreferences.IsEnabled(preferences), Is.True);
        Assert.That(UpNextPreferences.DefaultStatus(preferences), Is.EqualTo(MediaListStatus.Current));
    }

    [Test]
    public void Preferences_AStrayStoredStatusReadsAsTheDefault()
    {
        var preferences = new FakePreferences();
        preferences.Set(UpNextPreferences.StatusKey, nameof(MediaListStatus.Dropped));

        Assert.That(UpNextPreferences.DefaultStatus(preferences), Is.EqualTo(MediaListStatus.Current));
        Assert.Throws<ArgumentOutOfRangeException>(() => UpNextPreferences.SetDefaultStatus(preferences, MediaListStatus.Dropped));
    }

    // ── The offer ────────────────────────────────────────────────────

    [Test]
    public void Begin_WhenSwitchedOff_IsNoOffer()
    {
        var harness = new OfferHarness();
        UpNextPreferences.SetEnabled(harness.Preferences, false);

        Assert.That(harness.Offer.Begin(harness.Completed), Is.Null);
    }

    [Test]
    public async Task Sheet_AddsThePicksUnderTheChosenList_UnreleasedToPlanning()
    {
        var harness = new OfferHarness(
            Edge(MediaRelationKind.Sequel, 2),
            Edge(MediaRelationKind.SideStory, 3, status: "NOT_YET_RELEASED"));
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);

        using var scope = harness.BeginOffer();
        var added = await scope.OfferAsync();

        Assert.That(harness.Saved.Select(e => (e.MediaId, e.Status)), Is.EqualTo(
            [(2, (MediaListStatus?)MediaListStatus.Current), (3, MediaListStatus.Planning)]));
        Assert.That(added.Select(e => e.Id), Is.EqualTo([1002, 1003]));
        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["2 titles added"]));
    }

    [Test]
    public async Task ACompletedTitleMovedToPlanning_StartsFromZero_KeepingEverythingElse()
    {
        // Left at the total it sat in Plan to Watch reading 28/28 (agreed 2026-11-20: a clean "plan
        // to rewatch"). Score, notes and the rewatch count are the user's and stay.
        var done = OnList(2, MediaListStatus.Completed);
        done.Progress = 12;
        done.ProgressVolumes = 3;
        done.Repeat = 2;
        done.Notes = "loved it";
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2, configure: n => n.ViewerEntry = done));
        UpNextPreferences.SetDefaultStatus(harness.Preferences, MediaListStatus.Planning);
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);
        MediaListEntry? sent = null;
        harness.Client.When(c => c.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>()))
            .Do(call => sent = Snapshot(call.Arg<MediaListEntry>()));

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(sent?.Status, Is.EqualTo(MediaListStatus.Planning));
        Assert.That(sent?.Progress, Is.Zero);
        Assert.That(sent?.ProgressVolumes, Is.Zero);
        Assert.That(sent?.Score, Is.EqualTo(7));
        Assert.That(sent?.Notes, Is.EqualTo("loved it"));
        Assert.That(sent?.Repeat, Is.EqualTo(2));
    }

    // PR #224 review: the relations entry carried no media, so the shared "Watching never sits at its
    // total" step-back could not see the total and +1 was left dead.
    [TestCase(MediaListStatus.Paused, "ANIME")]
    [TestCase(MediaListStatus.Dropped, "ANIME")]
    [TestCase(MediaListStatus.Planning, "ANIME")]
    [TestCase(MediaListStatus.Paused, "MANGA")]
    public async Task AMoveToWatching_AtTheTotal_StepsBackOne(MediaListStatus from, string type)
    {
        var entry = OnList(2, from);
        entry.Progress = 12;
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2, type: type, status: "FINISHED", configure: n =>
        {
            n.ViewerEntry = entry;
            n.Episodes = 12;
            n.Chapters = 12;
        }));
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);
        MediaListEntry? sent = null;
        harness.Client.When(c => c.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>()))
            .Do(call => sent = Snapshot(call.Arg<MediaListEntry>()));

        using var scope = harness.BeginOffer(type == "MANGA" ? MangaCompleted() : null);
        await scope.OfferAsync();

        Assert.That(sent?.Status, Is.EqualTo(MediaListStatus.Current));
        Assert.That(sent?.Progress, Is.EqualTo(11));
    }

    [TestCase(MediaListStatus.Paused)]
    [TestCase(MediaListStatus.Dropped)]
    public async Task APausedOrDroppedTitleMovedToPlanning_KeepsItsProgress(MediaListStatus from)
    {
        // Only a finished title restarts: a paused or dropped one keeps its place for when it resumes.
        var entry = OnList(2, from);
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2, configure: n => n.ViewerEntry = entry));
        UpNextPreferences.SetDefaultStatus(harness.Preferences, MediaListStatus.Planning);
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);
        MediaListEntry? sent = null;
        harness.Client.When(c => c.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>()))
            .Do(call => sent = Snapshot(call.Arg<MediaListEntry>()));

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(sent?.Status, Is.EqualTo(MediaListStatus.Planning));
        Assert.That(sent?.Progress, Is.EqualTo(3));
    }

    [Test]
    public async Task Sheet_FollowsTheSettingsList_ForAddsAndMoves()
    {
        // Seen on device 2026-11-20: with Planning set, a completed sequel still read "Completed →
        // Rewatching". The Settings list now governs moves too.
        var harness = new OfferHarness(
            Edge(MediaRelationKind.Sequel, 2),
            Edge(MediaRelationKind.Sequel, 3, year: 2030, configure: n => n.ViewerEntry = OnList(3, MediaListStatus.Completed)),
            Edge(MediaRelationKind.SideStory, 4, configure: n => n.ViewerEntry = OnList(4, MediaListStatus.Dropped)));
        UpNextPreferences.SetDefaultStatus(harness.Preferences, MediaListStatus.Planning);
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(harness.Dialogs.LastUpNextCandidates?.Select(c => c.DestinationLabel), Is.EqualTo(
            ["→ Plan to Watch", "Completed → Plan to Watch", "Dropped → Plan to Watch"]));
        Assert.That(harness.Saved.Select(e => (e.MediaId, e.Status)), Is.EqualTo(
            [(2, (MediaListStatus?)MediaListStatus.Planning), (3, MediaListStatus.Planning), (4, MediaListStatus.Planning)]));
    }

    // ── What the logs show ───────────────────────────────────────────
    // A completion that offers nothing looks the same in the log whether the toggle was off or the
    // fetch found nothing, unless each says so. On device 2026-11-20 a "+1 asked me nothing" report
    // took a round of digging because the switched-off case logged nothing at all.

    [Test]
    public void Log_SaysWhenTheOfferIsSwitchedOff()
    {
        var harness = new OfferHarness();
        UpNextPreferences.SetEnabled(harness.Preferences, false);

        harness.Offer.Begin(harness.Completed);

        Assert.That(harness.Logger.Containing("UPNEXT off"), Has.Count.EqualTo(1));
    }

    [TestCase(false, "picked 0 of 1")]
    [TestCase(true, "picked 1 of 1")]
    public async Task Log_SaysWhatTheSheetWasAnsweredWith(bool pick, string expected)
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Dialogs.UpNextAnswer = candidates => pick ? new UpNextChoice(candidates) : null;

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(harness.Logger.Containing(expected), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Sheet_NotNow_ThenSaysSaved_OnceTheSheetHasClosed()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        var toastsWhileSheetUp = -1;
        harness.Dialogs.OnCall = call => toastsWhileSheetUp = harness.Feedback.Toasts.Count;
        harness.Dialogs.UpNextAnswer = _ => null;

        using var scope = harness.BeginOffer();
        await scope.OfferAsync(() => harness.Feedback.ShowToastAsync("Saved"));

        Assert.That(toastsWhileSheetUp, Is.Zero, "a toast draws over the sheet's buttons");
        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Saved"]));
    }

    [Test]
    public async Task Sheet_WhenTitlesArePicked_TheirToastIsTheOnlyOne()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);

        using var scope = harness.BeginOffer();
        await scope.OfferAsync(() => harness.Feedback.ShowToastAsync("Saved"));

        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Related-2 added to Watching"]));
    }

    [Test]
    public async Task Sheet_ThatFails_StillSaysSaved()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Dialogs.UpNextAnswer = _ => throw new InvalidOperationException("no current page");

        using var scope = harness.BeginOffer();
        var added = await scope.OfferAsync(() => harness.Feedback.ShowToastAsync("Saved"));

        Assert.That(added, Is.Empty);
        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Saved"]));
    }

    [Test]
    public async Task Sheet_NotNow_AddsNothing()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Dialogs.UpNextAnswer = _ => null;

        using var scope = harness.BeginOffer();
        var added = await scope.OfferAsync();

        Assert.That(added, Is.Empty);
        Assert.That(harness.Saved, Is.Empty);
    }

    [Test]
    public async Task NothingEligible_ShowsNothing()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Prequel, 2));

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(harness.Dialogs.Calls, Is.Empty);
        Assert.That(harness.Feedback.Snackbars, Is.Empty);
    }

    [Test]
    public async Task AFailedFetch_IsSilent()
    {
        // The completion already saved; a failure snackbar for an offer the user never asked for is noise.
        var harness = new OfferHarness();
        harness.Client.GetMediaRelationsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<MediaRelationEdge>>(new AniListApiException(ApiErrorKind.Network, "offline")));

        using var scope = harness.BeginOffer();
        var added = await scope.OfferAsync();

        Assert.That(added, Is.Empty);
        Assert.That(harness.Dialogs.Calls, Is.Empty);
        Assert.That(harness.Feedback.Snackbars, Is.Empty);
    }

    [Test]
    public async Task OneFailedAdd_IsReported_AndTheRestStillSave()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2), Edge(MediaRelationKind.Sequel, 3, year: 2030));
        harness.FailSaveFor(2);
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);

        using var scope = harness.BeginOffer();
        var added = await scope.OfferAsync();

        Assert.That(added.Select(e => e.MediaId), Is.EqualTo([3]));
        Assert.That(harness.Feedback.Snackbars, Has.Some.Contains("Couldn't add Related-2"));
        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Related-3 added to Watching"]));
    }

    [Test]
    public async Task ADeclinedCompletionConfirm_SpendsNoRequest()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        var entry = TestDataBuilder.Entry(mediaId: 1, progress: 12, episodes: 12);
        harness.Dialogs.ConfirmAnswer = false;

        using var scope = harness.BeginOffer(entry);
        var confirmed = await new ListEntryStatusFlow(harness.Dialogs).ApplyCompletionAsync(entry, scope);

        Assert.That(confirmed, Is.False);
        await harness.Client.DidNotReceive().GetMediaRelationsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheFetchStartsBehindTheRatingPrompt()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        var fetchedBeforeRating = false;
        harness.Dialogs.OnCall = call =>
        {
            if (call == nameof(IDialogService.ShowRatingAsync))
            {
                fetchedBeforeRating = harness.Client.ReceivedCalls()
                    .Any(c => c.GetMethodInfo().Name == nameof(IAniListClient.GetMediaRelationsAsync));
            }
        };

        using var scope = harness.BeginOffer();
        await new ListEntryStatusFlow(harness.Dialogs).ApplyStatusChangeAsync(harness.Completed, MediaListStatus.Completed, scope);

        Assert.That(fetchedBeforeRating, Is.True);
    }

    [Test]
    public void Candidates_IgnoreEdgesWithNoUsableNode()
    {
        var edges = new[]
        {
            new MediaRelationEdge { Kind = MediaRelationKind.Sequel, Node = null },
            new MediaRelationEdge { Kind = MediaRelationKind.Sequel, Node = Node(0) },
            Edge(MediaRelationKind.Sequel, 5),
        };

        Assert.That(Pick(edges).Select(c => c.Media.Id), Is.EqualTo([5]));
    }

    [Test]
    public void Begin_WithoutMedia_IsNoOffer()
    {
        var harness = new OfferHarness();

        Assert.That(harness.Offer.Begin(new MediaListEntry { MediaId = 1 }), Is.Null);
        Assert.That(harness.Offer.Begin(TestDataBuilder.Entry(mediaId: 0)), Is.Null);
    }

    [Test]
    public async Task ADisposedScope_OffersNothing()
    {
        // The flow ended (the save failed, or the page went away) before the offer was made.
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));

        var scope = harness.BeginOffer();
        scope.Dispose();
        var added = await scope.OfferAsync();

        Assert.That(added, Is.Empty);
        Assert.That(harness.Dialogs.Calls, Is.Empty);
        await harness.Client.DidNotReceive().GetMediaRelationsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Sheet_AnEmptyChoice_SavesNothing()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Dialogs.UpNextAnswer = _ => new UpNextChoice([]);

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(harness.Saved, Is.Empty);
        Assert.That(harness.Feedback.Toasts, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnAddTheServerDoesNotConfirm_IsReportedAsFailed(bool answersWithAnIdlessEntry)
    {
        // A create with no entry id did not really take; reporting it as added would be a false success.
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(answersWithAnIdlessEntry ? new MediaListEntry { Id = 0, MediaId = 2 } : null));
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);

        using var scope = harness.BeginOffer();
        var added = await scope.OfferAsync();

        Assert.That(added, Is.Empty);
        Assert.That(harness.Feedback.Toasts, Is.Empty);
        Assert.That(harness.Feedback.Snackbars, Has.Some.Contains("Couldn't add Related-2"));
    }

    [Test]
    public async Task AMove_SavesTheWholeEntryBack_WithOnlyItsStatusChanged()
    {
        // The overwrite this design exists to avoid: score, notes and the private flag survive.
        var planned = OnList(2, MediaListStatus.Planning);
        planned.Score = 8;
        planned.Notes = "from a friend";
        planned.Private = true;
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2, configure: n => n.ViewerEntry = planned));
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);
        MediaListEntry? sent = null;
        harness.Client.When(c => c.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>()))
            .Do(call => sent = Snapshot(call.Arg<MediaListEntry>()));

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(sent?.Id, Is.EqualTo(502), "the existing entry, not a new one");
        Assert.That(sent?.Status, Is.EqualTo(MediaListStatus.Current));
        Assert.That(sent?.Score, Is.EqualTo(8));
        Assert.That(sent?.Notes, Is.EqualTo("from a friend"));
        Assert.That(sent?.Private, Is.True);
        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Related-2 moved to Watching"]));
    }

    [Test]
    public async Task FinishingARewatch_OffersTheCompletedSequelAsARewatch_FromZero()
    {
        var done = OnList(2, MediaListStatus.Completed);
        done.Progress = 12;
        done.Repeat = 1;
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2, configure: n => n.ViewerEntry = done));
        harness.Completed.Status = MediaListStatus.Repeating;
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);
        MediaListEntry? sent = null;
        harness.Client.When(c => c.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>()))
            .Do(call => sent = Snapshot(call.Arg<MediaListEntry>()));

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(sent?.Status, Is.EqualTo(MediaListStatus.Repeating));
        Assert.That(sent?.Progress, Is.EqualTo(0));
        Assert.That(sent?.Repeat, Is.EqualTo(1), "AniList counts the rewatch when it is completed");
        // Seen on device 2026-11-20: the toast said "moved to Watching" because the save had already
        // written Rewatching into the candidate's own entry, which changed what it reported as its move.
        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Related-2 moved to Rewatching"]));
    }

    [Test]
    public async Task AFirstWatch_AlsoOffersTheCompletedSequelAsARewatch()
    {
        // Agreed on device 2026-11-20: whether the completion was itself a rewatch no longer matters.
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2, configure: n => n.ViewerEntry = OnList(2, MediaListStatus.Completed)));
        harness.Completed.Status = MediaListStatus.Current;

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(harness.Dialogs.LastUpNextCandidates?.Select(c => c.DestinationLabel), Is.EqualTo(["Completed → Rewatching"]));
    }

    [Test]
    public async Task AMixOfAddsAndMoves_SaysUpdated()
    {
        var harness = new OfferHarness(
            Edge(MediaRelationKind.Sequel, 2),
            Edge(MediaRelationKind.Sequel, 3, year: 2030, configure: n => n.ViewerEntry = OnList(3, MediaListStatus.Planning)));
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);

        using var scope = harness.BeginOffer();
        await scope.OfferAsync();

        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["2 titles updated"]));
    }

    // ── Wiring into the shared list flows ────────────────────────────

    [Test]
    public async Task Coordinator_OffersAfterTheCompletionSaves_ThenTellsTheHostOnce()
    {
        // The Library reloads on a status change: offering first means one reload, with the adds in it.
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        var order = new List<string>();
        harness.Dialogs.OnCall = call => order.Add(call);
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MarkCompleted;
        var coordinator = harness.Coordinator(onStatusChanged: () => order.Add("host"));

        await coordinator.ShowEntryMenuAsync(harness.Completed);

        Assert.That(order, Is.EqualTo([
            nameof(IDialogService.ShowEntryActionsAsync),
            nameof(IDialogService.ShowRatingAsync),
            nameof(IDialogService.ShowUpNextAsync),
            "host",
        ]));
        Assert.That(harness.Saved.Select(e => e.MediaId), Is.EqualTo([1, 2]));
    }

    [Test]
    public async Task Coordinator_WhenTheCompletionSaveFails_OffersNothing()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.FailSaveFor(1);
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MarkCompleted;

        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);

        Assert.That(harness.Dialogs.Calls, Has.None.EqualTo(nameof(IDialogService.ShowUpNextAsync)));
    }

    [Test]
    public async Task Coordinator_ARetriedCompletionThatSaves_StillOffers()
    {
        // PR #224 review: the first attempt's offer died with the failed save, so a Retry that went
        // through completed the title silently.
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.FailNextSaveFor(1);
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MarkCompleted;
        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);
        Assert.That(harness.Dialogs.Calls, Has.None.EqualTo(nameof(IDialogService.ShowUpNextAsync)));

        if (harness.Feedback.LastSnackbarAction is not { } retry)
        {
            throw new AssertionException("The failed save offered no Retry.");
        }

        retry();

        Assert.That(harness.Dialogs.Calls, Has.One.EqualTo(nameof(IDialogService.ShowUpNextAsync)));
        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Saved"]), "Not now after the retry: the save's own toast");
    }

    [Test]
    public async Task Coordinator_MovingToAnotherList_IsNoOffer()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MoveToList;
        harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(MediaListStatus.Paused);

        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);

        await harness.Client.DidNotReceive().GetMediaRelationsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Coordinator_MovingToCompleted_Offers()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MoveToList;
        harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(MediaListStatus.Completed);

        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);

        Assert.That(harness.Dialogs.Calls, Has.Some.EqualTo(nameof(IDialogService.ShowUpNextAsync)));
    }

    [Test]
    public async Task Coordinator_AddingStraightToCompleted_Offers()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        var candidate = TestDataBuilder.Entry(mediaId: 1, status: null);
        candidate.Id = 0;
        harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(MediaListStatus.Completed);

        await harness.Coordinator().ShowAddToListAsync(candidate);

        Assert.That(harness.Dialogs.Calls, Has.Some.EqualTo(nameof(IDialogService.ShowUpNextAsync)));
    }

    // ── The saved toast vs the sheet ─────────────────────────────────
    // An Android toast draws over every window, so one shown just before the sheet sat on its buttons
    // (seen on device, #103). The sheet only opens once the completion has saved, so it stands in for
    // the toast; with no sheet, the toast is the only word the save went through and must still show.

    [TestCase(MediaListEntryAction.MarkCompleted, null)]
    [TestCase(MediaListEntryAction.MoveToList, MediaListStatus.Completed)]
    public async Task Coordinator_HoldsTheSavedToastUntilTheSheetCloses(MediaListEntryAction action, MediaListStatus? moveTo)
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        harness.Dialogs.EntryActionAnswer = action;
        if (moveTo is { } status)
        {
            harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(status);
        }

        var toastsWhileSheetUp = -1;
        harness.Dialogs.OnCall = call =>
        {
            if (call == nameof(IDialogService.ShowUpNextAsync))
            {
                toastsWhileSheetUp = harness.Feedback.Toasts.Count;
            }
        };

        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);

        Assert.That(toastsWhileSheetUp, Is.Zero, "a toast draws over the sheet's buttons");
        Assert.That(harness.Feedback.Toasts, Has.Count.EqualTo(1), "Not now: the completion's own toast, after the sheet");
    }

    [Test]
    public async Task Coordinator_AddingStraightToCompleted_WithASheet_HasNoAddedToast()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        var candidate = TestDataBuilder.Entry(mediaId: 1, status: null);
        candidate.Id = 0;
        harness.Dialogs.MoveToListAnswer = MoveToListChoice.To(MediaListStatus.Completed);
        harness.Dialogs.UpNextAnswer = candidates => new UpNextChoice(candidates);

        await harness.Coordinator().ShowAddToListAsync(candidate);

        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Related-2 added to Watching"]), "only the sheet's own result");
    }

    [Test]
    public async Task Coordinator_WithNothingToOffer_StillSaysSaved()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Prequel, 2));
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MarkCompleted;

        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);

        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Saved"]));
    }

    [Test]
    public async Task Coordinator_WhenTheOfferCannotLoad_StillSaysSaved()
    {
        var harness = new OfferHarness();
        harness.Client.GetMediaRelationsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<MediaRelationEdge>>(new AniListApiException(ApiErrorKind.Network, "offline")));
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MarkCompleted;

        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);

        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Saved"]));
    }

    [Test]
    public async Task Coordinator_WithTheOfferSwitchedOff_StillSaysSaved()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Sequel, 2));
        UpNextPreferences.SetEnabled(harness.Preferences, false);
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MarkCompleted;

        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);

        Assert.That(harness.Feedback.Toasts, Is.EqualTo(["Saved"]));
    }

    [Test]
    public async Task Coordinator_WhenTheCompletionSaveFails_ShowsNoSavedToast()
    {
        var harness = new OfferHarness(Edge(MediaRelationKind.Prequel, 2));
        harness.FailSaveFor(1);
        harness.Dialogs.EntryActionAnswer = MediaListEntryAction.MarkCompleted;

        await harness.Coordinator().ShowEntryMenuAsync(harness.Completed);

        Assert.That(harness.Feedback.Toasts, Is.Empty);
        Assert.That(harness.Feedback.Snackbars, Has.Some.Contains("Failed to save"));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static MediaRelationEdge Edge(
        MediaRelationKind kind, int id, string type = "ANIME", string? status = null, int? year = 2025, Action<RelatedMedia>? configure = null)
    {
        var node = Node(id, type, status, year);
        configure?.Invoke(node);
        return new MediaRelationEdge { Kind = kind, Node = node };
    }

    /// <summary>An entry as the relations query returns it: the whole thing, with an id of its own.</summary>
    private static IReadOnlyList<UpNextCandidate> Pick(
        MediaRelationEdge[] edges,
        MediaKind kind = MediaKind.Anime,
        bool adult = false,
        MediaListStatus setting = MediaListStatus.Current)
        => UpNextCandidates.Select(kind, edges, adult, setting);

    private static UpNextCandidate One(
        MediaRelationEdge edge, MediaListStatus setting = MediaListStatus.Current, MediaKind kind = MediaKind.Anime)
        => Pick([edge], kind, setting: setting) is [var only]
            ? only
            : throw new AssertionException($"Expected media {edge.Node?.Id} to be offered.");

    /// <summary>A finished manga to complete, so only manga relations are offered.</summary>
    private static MediaListEntry MangaCompleted() => TestDataBuilder.Entry(mediaId: 1, mediaType: "MANGA", chapters: 30);

    private static MediaRelationEdge Planned(int id)
        => Edge(MediaRelationKind.Sequel, id, status: "FINISHED", configure: n => n.ViewerEntry = OnList(id, MediaListStatus.Planning));

    private static MediaListEntry OnList(int mediaId, MediaListStatus status) =>
        new() { Id = 500 + mediaId, MediaId = mediaId, Status = status, Progress = 3, Score = 7, Repeat = 0 };

    /// <summary>What was sent, as it was at the call: the offer goes on to write the saved id back into the same object.</summary>
    private static MediaListEntry Snapshot(MediaListEntry e) => new()
    {
        Id = e.Id, MediaId = e.MediaId, Status = e.Status, Progress = e.Progress, ProgressVolumes = e.ProgressVolumes, Score = e.Score,
        Repeat = e.Repeat, Notes = e.Notes, Private = e.Private,
    };

    private static RelatedMedia Node(int id, string type = "ANIME", string? status = null, int? year = null) => new()
    {
        Id = id,
        Type = type,
        Status = status,
        Title = new MediaTitle { Romaji = $"Related-{id}" },
        StartDate = year is null ? null : new MediaDate { Year = year },
    };

    private sealed class OfferHarness
    {
        private readonly HashSet<int> _failingSaves = [];
        private readonly HashSet<int> _failingOnce = [];

        public OfferHarness(params MediaRelationEdge[] relations)
        {
            Client.GetMediaRelationsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(relations);
            Client.SaveMediaListEntryAsync(Arg.Any<MediaListEntry>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var entry = call.Arg<MediaListEntry>();
                Saved.Add(new MediaListEntry { MediaId = entry.MediaId, Status = entry.Status });
                return _failingSaves.Contains(entry.MediaId) || _failingOnce.Remove(entry.MediaId)
                    ? Task.FromException<MediaListEntry?>(new AniListApiException(ApiErrorKind.Network, "offline"))
                    : Task.FromResult<MediaListEntry?>(new MediaListEntry { Id = 1000 + entry.MediaId, MediaId = entry.MediaId, Status = entry.Status });
            });

            Offer = new UpNextOffer(Client, Dialogs, Feedback, Preferences, Logger);
        }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public ScriptedDialogService Dialogs { get; } = new();

        public RecordingUserFeedback Feedback { get; } = new();

        public FakePreferences Preferences { get; } = new();

        public RecordingLogger<UpNextOffer> Logger { get; } = new();

        public UpNextOffer Offer { get; }

        public MediaListEntry Completed { get; } = TestDataBuilder.Entry(mediaId: 1, episodes: 12);

        /// <summary>Every save the client was asked for, in order, as (media, status).</summary>
        public List<MediaListEntry> Saved { get; } = [];

        public UpNextScope BeginOffer(MediaListEntry? completing = null)
            => Offer.Begin(completing ?? Completed) ?? throw new AssertionException("Begin returned no offer; is the setting off?");

        public void FailSaveFor(int mediaId) => _failingSaves.Add(mediaId);

        /// <summary>The next save of this media fails; the one after it (a Retry) goes through.</summary>
        public void FailNextSaveFor(int mediaId) => _failingOnce.Add(mediaId);

        public EntryActionCoordinator Coordinator(Action? onStatusChanged = null) => new(
            Client,
            new ErrorReportService(NullLogger<ErrorReportService>.Instance),
            Dialogs,
            Feedback,
            new ListEntryStatusFlow(Dialogs),
            Offer,
            NullLogger.Instance,
            new EntryActionHost
            {
                OpenDetailsAsync = _ => Task.CompletedTask,
                OnEntryStatusChangedAsync = _ => { onStatusChanged?.Invoke(); return Task.CompletedTask; },
            });
    }
}
