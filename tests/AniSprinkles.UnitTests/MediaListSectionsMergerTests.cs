using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace AniSprinkles.UnitTests;

[NonParallelizable]
public class MediaListSectionsMergerTests
{
    public MediaListSectionsMergerTests() => TestDataBuilder.ResetAppSettings();

    // ── Empty/populated permutations ────────────────────────────────────

    [Test]
    public void Merge_EmptyToEmpty_IsNoOp()
    {
        // Arrange
        var sections = new ObservableCollection<MediaListSection>();

        // Act
        var result = MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(),
            sectionOrder: [],
            displayAdultContent: true,
            SortField.LastUpdated,
            sortAscending: false,
            filterText: "");

        // Assert
        Assert.That(sections, Is.Empty);
        Assert.That(result.SectionsAdded, Is.EqualTo(0));
        Assert.That(result.EntriesAdded, Is.EqualTo(0));
    }

    [Test]
    public void Merge_EmptyToPopulated_AddsSectionsAndEntries()
    {
        // Arrange
        var sections = new ObservableCollection<MediaListSection>();
        var groups = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(2)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(3)));

        // Act
        var result = MediaListSectionsMerger.Merge(
            sections, groups, [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(sections.Count, Is.EqualTo(2));
        Assert.That(sections[0].Title, Is.EqualTo("Watching"));
        Assert.That(sections[1].Title, Is.EqualTo("Completed"));
        Assert.That(sections[0].TotalCount, Is.EqualTo(2));
        Assert.That(sections[1].TotalCount, Is.EqualTo(1));
        Assert.That(result.SectionsAdded, Is.EqualTo(2));
        Assert.That(result.EntriesAdded, Is.EqualTo(3));
    }

    [Test]
    public void Merge_PopulatedToEmpty_RemovesAllSections()
    {
        // Arrange
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1))));

        // Act
        var result = MediaListSectionsMerger.Merge(
            sections, TestDataBuilder.Groups(), [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(sections, Is.Empty);
        Assert.That(result.SectionsRemoved, Is.EqualTo(1));
        Assert.That(result.EntriesRemoved, Is.EqualTo(1));
    }

    [Test]
    public void Merge_UnchangedData_FiresNoCollectionResetOnOuterOrSections()
    {
        // The whole point of this refactor: repeated pull-to-refresh on identical data must
        // not emit Reset events that force RecyclerView to rebind every cell.

        // Arrange
        var groups = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(2)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(3)));
        var sections = TestDataBuilder.BuildInitial(groups);

        var outerChanges = 0;
        var sectionResets = 0;
        sections.CollectionChanged += (_, _) => outerChanges++;
        foreach (var section in sections)
        {
            section.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    sectionResets++;
                }
            };
        }

        // Rebuild a fresh set of entries with identical display data but fresh instances —
        // matches what AniListClient.MapEntry does on every fetch.
        var fresh = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(2)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(3)));

        // Act
        MediaListSectionsMerger.Merge(sections, fresh, [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(outerChanges, Is.EqualTo(0));
        Assert.That(sectionResets, Is.EqualTo(0));
    }

    // ── Entry-level diffs (single section) ──────────────────────────────

    [Test]
    public void Merge_AddsNewEntryToExistingSection()
    {
        // Arrange
        var groups = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)));
        var sections = TestDataBuilder.BuildInitial(groups);
        var watching = sections[0];

        var updated = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(2)));

        // Act
        var result = MediaListSectionsMerger.Merge(sections, updated, [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(sections[0], Is.SameAs(watching));
        Assert.That(watching.TotalCount, Is.EqualTo(2));
        Assert.That(result.EntriesAdded, Is.EqualTo(1));
    }

    [Test]
    public void Merge_RemovesEntryNoLongerPresent()
    {
        // Arrange
        var groups = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(2)));
        var sections = TestDataBuilder.BuildInitial(groups);

        var updated = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)));

        // Act
        var result = MediaListSectionsMerger.Merge(sections, updated, [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(sections[0].TotalCount, Is.EqualTo(1));
        Assert.That(sections[0].AllItems[0].MediaId, Is.EqualTo(1));  // id 2 was the one removed
        Assert.That(result.EntriesRemoved, Is.EqualTo(1));
    }

    [Test]
    public void Merge_ProgressChangeOnly_PreservesReference_FiresPropertyChanged_NoReset()
    {
        // Arrange
        var original = TestDataBuilder.Entry(1, progress: 3);
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", original)));

        var watching = sections[0];
        var sectionReset = false;
        var progressChanged = false;
        watching.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                sectionReset = true;
            }
        };
        original.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaListEntry.Progress))
            {
                progressChanged = true;
            }
        };

        var bumped = TestDataBuilder.Entry(1, progress: 4);

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", bumped)),
            [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(watching.AllItems[0], Is.SameAs(original));  // reference preserved
        Assert.That(original.Progress, Is.EqualTo(4));
        Assert.That(progressChanged, Is.True);
        Assert.That(sectionReset, Is.False);
    }

    [Test]
    public void Merge_ScoreChangeOnly_FiresPropertyChanged()
    {
        // Arrange
        var original = TestDataBuilder.Entry(1, score: 7.5);
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", original)));

        var scoreChanged = false;
        original.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaListEntry.Score))
            {
                scoreChanged = true;
            }
        };

        var bumped = TestDataBuilder.Entry(1, score: 9.0);

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", bumped)),
            [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(original.Score, Is.EqualTo(9.0));
        Assert.That(scoreChanged, Is.True);
    }

    [Test]
    public void Merge_UpdatedAtChange_WithLastUpdatedSort_ReOrdersSection()
    {
        // Arrange — entry 1 updated later than entry 2; descending LastUpdated sort puts 1 first.
        var e1 = TestDataBuilder.Entry(1, updatedAt: DateTimeOffset.FromUnixTimeSeconds(200));
        var e2 = TestDataBuilder.Entry(2, updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));
        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1, e2)),
            sortField: SortField.LastUpdated,
            sortAscending: false);
        Assert.That(sections[0][0].MediaId, Is.EqualTo(1)); // pre-condition

        // Now entry 2 gets bumped to a newer UpdatedAt than entry 1.
        var e1Same = TestDataBuilder.Entry(1, updatedAt: DateTimeOffset.FromUnixTimeSeconds(200));
        var e2Bumped = TestDataBuilder.Entry(2, updatedAt: DateTimeOffset.FromUnixTimeSeconds(300));

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1Same, e2Bumped)),
            [], true, SortField.LastUpdated, false, "");

        // Assert — entry 2 now sorts to the top of the visible, sorted collection.
        Assert.That(sections[0][0].MediaId, Is.EqualTo(2));
        Assert.That(sections[0][1].MediaId, Is.EqualTo(1));
    }

    [Test]
    public void Merge_ScoreChange_WithScoreSort_ReOrdersSection()
    {
        // Arrange — sorted descending by Score, entry 1 (9.0) above entry 2 (5.0).
        var e1 = TestDataBuilder.Entry(1, score: 9.0);
        var e2 = TestDataBuilder.Entry(2, score: 5.0);
        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1, e2)),
            sortField: SortField.Score,
            sortAscending: false);
        Assert.That(sections[0][0].MediaId, Is.EqualTo(1)); // pre-condition

        // Entry 2's score is raised above entry 1's.
        var e1Same = TestDataBuilder.Entry(1, score: 9.0);
        var e2Bumped = TestDataBuilder.Entry(2, score: 10.0);

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1Same, e2Bumped)),
            [], true, SortField.Score, false, "");

        // Assert
        Assert.That(sections[0][0].MediaId, Is.EqualTo(2));
    }

    [Test]
    public void Merge_WeightedScoreChange_WithWeightedScoreSort_ReOrdersSection()
    {
        // Arrange — sorted descending by Media.WeightedScore, entry 1 (90) above entry 2 (70).
        var e1 = TestDataBuilder.Entry(1);
        e1.Media!.WeightedScore = 90;
        var e2 = TestDataBuilder.Entry(2);
        e2.Media!.WeightedScore = 70;

        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1, e2)),
            sortField: SortField.WeightedScore,
            sortAscending: false);
        Assert.That(sections[0][0].MediaId, Is.EqualTo(1)); // pre-condition

        // Entry 2's average is raised above entry 1's.
        var e1Same = TestDataBuilder.Entry(1);
        e1Same.Media!.WeightedScore = 90;
        var e2Bumped = TestDataBuilder.Entry(2);
        e2Bumped.Media!.WeightedScore = 95;

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1Same, e2Bumped)),
            [], true, SortField.WeightedScore, false, "");

        // Assert
        Assert.That(sections[0][0].MediaId, Is.EqualTo(2));
    }

    [Test]
    public void Merge_UpdatedAtChange_WithTitleSort_DoesNotTriggerSectionReset()
    {
        // When the active sort is Title, an UpdatedAt bump must NOT force a section re-sort/Reset.
        // This is the scoping guard that prevents the common "progress bump under Title sort" case
        // from regressing the pull-to-refresh perf win.

        // Arrange
        var e1 = TestDataBuilder.Entry(1, title: "Alpha", updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));
        var e2 = TestDataBuilder.Entry(2, title: "Beta", updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));
        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1, e2)),
            sortField: SortField.Title,
            sortAscending: true);

        var sectionResets = 0;
        sections[0].CollectionChanged += (_, ev) =>
        {
            if (ev.Action == NotifyCollectionChangedAction.Reset)
            {
                sectionResets++;
            }
        };

        // Only UpdatedAt changes (titles unchanged).
        var e1Bumped = TestDataBuilder.Entry(1, title: "Alpha", updatedAt: DateTimeOffset.FromUnixTimeSeconds(999));
        var e2Same = TestDataBuilder.Entry(2, title: "Beta", updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1Bumped, e2Same)),
            [], true, SortField.Title, true, "");

        // Assert
        Assert.That(sectionResets, Is.EqualTo(0));
    }

    [Test]
    public void Merge_CoverImageUrlChanged_TriggersSectionReset_ReferencePreserved()
    {
        // Arrange
        var original = TestDataBuilder.Entry(1, coverMedium: "https://img/old");
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", original)));

        var sectionReset = false;
        sections[0].CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                sectionReset = true;
            }
        };

        var refreshed = TestDataBuilder.Entry(1, coverMedium: "https://img/new");

        // Act
        var result = MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", refreshed)),
            [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(sections[0].AllItems[0], Is.SameAs(original));
        Assert.That(original.Media?.CoverImage?.Medium, Is.EqualTo("https://img/new"));
        Assert.That(sectionReset, Is.True);
        Assert.That(result.SectionsNeedingReset, Is.EqualTo(1));
    }

    [Test]
    public void Merge_NextAiringEpisodeChanged_TriggersReset()
    {
        // Arrange
        var original = TestDataBuilder.Entry(1, nextAiringEpisode: 5, nextAiringAt: 1000);
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", original)));

        var refreshed = TestDataBuilder.Entry(1, nextAiringEpisode: 6, nextAiringAt: 2000);

        // Act
        var result = MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", refreshed)),
            [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(result.SectionsNeedingReset, Is.EqualTo(1));
    }

    // ── Section-level diffs ─────────────────────────────────────────────

    [Test]
    public void Merge_NewSectionAppears_InsertedAtOrderedIndex()
    {
        // Arrange
        var sectionOrder = new[] { "Watching", "Paused", "Completed" };
        var initial = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(3))),
            sectionOrder);

        var updated = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)),
            TestDataBuilder.Group("Paused", TestDataBuilder.Entry(2)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(3)));

        // Act
        MediaListSectionsMerger.Merge(initial, updated, sectionOrder, true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(initial.Count, Is.EqualTo(3));
        Assert.That(initial[0].Title, Is.EqualTo("Watching"));
        Assert.That(initial[1].Title, Is.EqualTo("Paused"));
        Assert.That(initial[2].Title, Is.EqualTo("Completed"));
    }

    [Test]
    public void Merge_NewSection_IsFullyMaterializedWhenPublishedToOuterCollection()
    {
        // Regression: previously the merger added a brand-new section to the outer
        // ObservableCollection while its bulk-update scope was still open, so the
        // first CollectionChanged observer saw a section whose visible items hadn't
        // been materialized yet (FilteredCount=0, DisplayCount="0/N") until the
        // outer finally block disposed all scopes.

        // Arrange
        var sectionOrder = new[] { "Watching" };
        var initial = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(), sectionOrder);

        var updated = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(2)));

        var observedTotalCount = -1;
        var observedFilteredCount = -1;
        var observedDisplayCount = string.Empty;
        var observedVisibleCount = -1;
        initial.CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
            {
                return;
            }

            foreach (MediaListSection section in e.NewItems)
            {
                if (section.Title != "Watching")
                {
                    continue;
                }

                observedTotalCount = section.TotalCount;
                observedFilteredCount = section.FilteredCount;
                observedDisplayCount = section.DisplayCount;
                observedVisibleCount = section.Count;
            }
        };

        // Act
        MediaListSectionsMerger.Merge(
            initial,
            updated,
            sectionOrder,
            displayAdultContent: true,
            SortField.LastUpdated,
            sortAscending: false,
            filterText: "");

        // Assert — section is default-expanded (first section in an empty list) and fully materialized
        Assert.That(observedTotalCount, Is.EqualTo(2));
        Assert.That(observedFilteredCount, Is.EqualTo(2));
        Assert.That(observedDisplayCount, Is.EqualTo("2"));
        Assert.That(observedVisibleCount, Is.EqualTo(2));
    }

    [Test]
    public void Merge_SectionBecomesEmpty_IsRemoved()
    {
        // Arrange
        var initial = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)),
            TestDataBuilder.Group("Dropped", TestDataBuilder.Entry(2))));

        var updated = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)));

        // Act
        MediaListSectionsMerger.Merge(initial, updated, [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(initial, Has.Exactly(1).Items);
        Assert.That(initial[0].Title, Is.EqualTo("Watching"));
    }

    [Test]
    public void Merge_SectionOrderChanged_UsesMoveNotRemoveAdd()
    {
        // Arrange
        var initialOrder = new[] { "Watching", "Completed" };
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(2))),
            initialOrder);

        var watching = sections[0];
        var completed = sections[1];

        var moves = 0;
        var otherStructuralChanges = 0;
        sections.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Move)
            {
                moves++;
            }
            else if (e.Action != NotifyCollectionChangedAction.Reset)
            {
                otherStructuralChanges++;
            }
        };

        var newOrder = new[] { "Completed", "Watching" };
        var updated = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(2)));

        // Act
        MediaListSectionsMerger.Merge(sections, updated, newOrder, true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(sections[0], Is.SameAs(completed));
        Assert.That(sections[1], Is.SameAs(watching));
        Assert.That(moves >= 1, Is.True);
        Assert.That(otherStructuralChanges, Is.EqualTo(0));
    }

    [Test]
    public void Merge_SectionIsExpandedState_PreservedAcrossMerge()
    {
        // Arrange
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1))));
        sections[0].IsExpanded = false;

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(2))),
            [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(sections[0].IsExpanded, Is.False);
    }

    // ── Cross-section moves ─────────────────────────────────────────────

    [Test]
    public void Merge_EntryMovesBetweenSections_PreservesReference()
    {
        // Arrange
        var original = TestDataBuilder.Entry(1, status: MediaListStatus.Current);
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", original),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(2))));

        var moved = TestDataBuilder.Entry(1, status: MediaListStatus.Completed);

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(
                TestDataBuilder.Group("Watching"),  // empty — will be removed
                TestDataBuilder.Group("Completed", TestDataBuilder.Entry(2), moved)),
            [], true, SortField.LastUpdated, false, "");

        // Assert
        var completed = sections.Single(s => s.Title == "Completed");
        var movedEntry = completed.AllItems.Single(e => e.MediaId == 1);
        Assert.That(movedEntry, Is.SameAs(original));  // entry instance preserved across sections
        Assert.That(original.Status, Is.EqualTo(MediaListStatus.Completed));
        Assert.That(sections, Has.None.Matches<MediaListSection>(s => s is { Title: "Watching" }));
    }

    [Test]
    public void Merge_MultipleSimultaneousMoves_AllHandled()
    {
        // Arrange
        var a = TestDataBuilder.Entry(1);
        var b = TestDataBuilder.Entry(2);
        var c = TestDataBuilder.Entry(3);

        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", a, b, c)));

        var aMoved = TestDataBuilder.Entry(1);
        var bMoved = TestDataBuilder.Entry(2);

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(
                TestDataBuilder.Group("Watching", c),
                TestDataBuilder.Group("Completed", aMoved, bMoved)),
            [], true, SortField.LastUpdated, false, "");

        // Assert
        var watching = sections.Single(s => s.Title == "Watching");
        var completed = sections.Single(s => s.Title == "Completed");
        Assert.That(watching.AllItems, Has.Exactly(1).Items);
        Assert.That(watching.AllItems[0], Is.SameAs(c));
        Assert.That(completed.AllItems.Count, Is.EqualTo(2));
        Assert.That(completed.AllItems, Does.Contain(a));
        Assert.That(completed.AllItems, Does.Contain(b));
    }

    // ── Filter / sort interaction ────────────────────────────────────────

    [Test]
    public void Merge_WithActiveFilter_FilterReappliedToTouchedSections()
    {
        // Arrange
        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching",
                TestDataBuilder.Entry(1, title: "Apple"),
                TestDataBuilder.Entry(2, title: "Banana"))),
            filterText: "ban");

        Assert.That(sections[0], Has.Exactly(1).Items);  // pre-condition: only Banana visible under active filter

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching",
                TestDataBuilder.Entry(1, title: "Apple"),
                TestDataBuilder.Entry(2, title: "Banana"),
                TestDataBuilder.Entry(3, title: "Banshee"))),
            [], true, SortField.Title, true, "ban");

        // Assert
        Assert.That(sections[0].TotalCount, Is.EqualTo(3));
        Assert.That(sections[0].Count, Is.EqualTo(2));  // visible: Banana + Banshee
    }

    [Test]
    public void Merge_WithActiveFilter_RefreshesMembershipWhenNonDisplayedTitleChanges()
    {
        // MatchesFilter searches English/Romaji/Native, but DisplayTitle only resolves to one of
        // them (Romaji by default). A change to English under a Romaji display would previously
        // leave filter membership stale because MediaDisplayChanged only compares DisplayTitle.
        // Arrange
        var entry = new MediaListEntry
        {
            Id = 10,
            MediaId = 1,
            Status = MediaListStatus.Current,
            Media = new Media
            {
                Id = 1,
                Title = new MediaTitle { English = "Apple", Romaji = "Ringo" },
                CoverImage = new MediaCoverImage { Medium = "https://img/1" },
            },
        };
        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", entry)),
            filterText: "app");

        Assert.That(sections[0], Has.Exactly(1).Items);  // pre-condition: English title matches "app"

        // Act — refresh with the same MediaId but a new English title; Romaji (DisplayTitle) is unchanged.
        var refreshed = new MediaListEntry
        {
            Id = 10,
            MediaId = 1,
            Status = MediaListStatus.Current,
            Media = new Media
            {
                Id = 1,
                Title = new MediaTitle { English = "Grape", Romaji = "Ringo" },
                CoverImage = new MediaCoverImage { Medium = "https://img/1" },
            },
        };
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", refreshed)),
            [], true, SortField.LastUpdated, false, "app");

        // Assert — entry is still in the backing store but filter no longer matches.
        Assert.That(sections[0].TotalCount, Is.EqualTo(1));
        Assert.That(sections[0], Is.Empty);
    }

    // ── Adult content / reorder helpers ──────────────────────────────────

    [Test]
    public void OrderAndFilterGroups_DropsAdultEntriesWhenDisabled()
    {
        // Arrange
        var adult = TestDataBuilder.Entry(1, isAdult: true);
        var safe = TestDataBuilder.Entry(2, isAdult: false);
        var groups = TestDataBuilder.Groups(TestDataBuilder.Group("Watching", adult, safe));

        // Act
        var filtered = MediaListSectionsMerger.OrderAndFilterGroups(groups, [], displayAdultContent: false);

        // Assert
        Assert.That(filtered, Has.Exactly(1).Items);
        Assert.That(filtered[0].Entries, Has.Exactly(1).Items);
        Assert.That(filtered[0].Entries[0].MediaId, Is.EqualTo(2));
    }

    [Test]
    public void OrderAndFilterGroups_DropsNowEmptySections_WhenAdultFilterEmptiesThem()
    {
        // Arrange
        var groups = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1, isAdult: true)));

        // Act
        var filtered = MediaListSectionsMerger.OrderAndFilterGroups(groups, [], displayAdultContent: false);

        // Assert
        Assert.That(filtered, Is.Empty);
    }

    [Test]
    public void OrderAndFilterGroups_UnknownSectionsGoToEnd()
    {
        // Arrange
        var order = new[] { "Watching", "Completed" };
        var groups = TestDataBuilder.Groups(
            TestDataBuilder.Group("CustomList", TestDataBuilder.Entry(9)),
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(2)));

        // Act
        var ordered = MediaListSectionsMerger.OrderAndFilterGroups(groups, order, displayAdultContent: true);

        // Assert
        Assert.That(ordered.Select(g => g.Name), Is.EqualTo(["Watching", "Completed", "CustomList"]));
    }

    // ── MediaDisplayChanged primitive ────────────────────────────────────

    [Test]
    public void MediaDisplayChanged_BothNull_ReturnsFalse()
    {
        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(null, null);

        // Assert
        Assert.That(changed, Is.False);
    }

    [Test]
    public void MediaDisplayChanged_OneNull_ReturnsTrue()
    {
        // Act & Assert (two symmetric cases; see also the [TestCase] refactor opportunity)
        Assert.That(MediaListSectionsMerger.MediaDisplayChanged(null, new Media()), Is.True);
        Assert.That(MediaListSectionsMerger.MediaDisplayChanged(new Media(), null), Is.True);
    }

    [Test]
    public void MediaDisplayChanged_SameCoverAndAiring_ReturnsFalse()
    {
        // Arrange
        var a = new Media { CoverImage = new MediaCoverImage { Medium = "u" }, Episodes = 12 };
        var b = new Media { CoverImage = new MediaCoverImage { Medium = "u" }, Episodes = 12 };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.False);
    }

    [Test]
    public void MediaDisplayChanged_EpisodesDifferent_ReturnsTrue()
    {
        // Arrange
        var a = new Media { Episodes = 12 };
        var b = new Media { Episodes = 13 };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.True);
    }

    [Test]
    public void MediaDisplayChanged_FormatDifferent_ReturnsTrue()
    {
        // Arrange — list cell binds Media.Format via FormatIconBadge, so a
        // change must trigger a section Reset to refresh the badge.
        var a = new Media { Format = "TV_SHORT" };
        var b = new Media { Format = "TV" };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.True);
    }

    [Test]
    public void MediaDisplayChanged_SameFormat_ReturnsFalse()
    {
        // Arrange
        var a = new Media { Format = "TV" };
        var b = new Media { Format = "TV" };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.False);
    }

    [Test]
    public void MediaDisplayChanged_DisplayTitleDifferent_ReturnsTrue()
    {
        // Arrange — MediaDisplayChanged compares DisplayTitle directly, but DisplayTitle is derived
        // from AppSettings.TitleLanguage. Romaji is the test default (set by ResetAppSettings), so
        // varying Title.Romaji varies DisplayTitle.
        var a = new Media { Title = new MediaTitle { Romaji = "Attack on Titan" } };
        var b = new Media { Title = new MediaTitle { Romaji = "Shingeki no Kyojin" } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.True);
    }

    [Test]
    public void MediaDisplayChanged_SameDisplayTitle_ReturnsFalse()
    {
        // Arrange
        var a = new Media { Title = new MediaTitle { Romaji = "Same" } };
        var b = new Media { Title = new MediaTitle { Romaji = "Same" } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.False);
    }

    [Test]
    public void MediaDisplayChanged_CoverLargeDifferent_ReturnsTrue()
    {
        // Arrange — MediaDetails opens a larger cover; MyAnime grid layout binds .Large too.
        var a = new Media { CoverImage = new MediaCoverImage { Large = "https://img/old" } };
        var b = new Media { CoverImage = new MediaCoverImage { Large = "https://img/new" } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.True);
    }

    [Test]
    public void MediaDisplayChanged_NextAiringEpisodeChanged_ReturnsTrue()
    {
        // Arrange
        var a = new Media { NextAiringEpisode = new MediaAiringEpisode { Episode = 5, AiringAt = 1000 } };
        var b = new Media { NextAiringEpisode = new MediaAiringEpisode { Episode = 6, AiringAt = 2000 } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.True);
    }

    [Test]
    public void MediaDisplayChanged_SameNextAiringEpisode_ReturnsFalse()
    {
        // Arrange
        var a = new Media { NextAiringEpisode = new MediaAiringEpisode { Episode = 5, AiringAt = 1000 } };
        var b = new Media { NextAiringEpisode = new MediaAiringEpisode { Episode = 5, AiringAt = 1000 } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.False);
    }

    [Test]
    public void MediaDisplayChanged_SameEpisodes_ReturnsFalse()
    {
        // Arrange — negative complement to MediaDisplayChanged_EpisodesDifferent_ReturnsTrue.
        var a = new Media { Episodes = 12 };
        var b = new Media { Episodes = 12 };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.That(changed, Is.False);
    }

    // ── Reference preservation regression guard ──────────────────────────

    [Test]
    public void Merge_AllSurvivingEntries_KeepOriginalReferences()
    {
        // Arrange
        var e1 = TestDataBuilder.Entry(1);
        var e2 = TestDataBuilder.Entry(2);
        var e3 = TestDataBuilder.Entry(3);
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", e1, e2),
            TestDataBuilder.Group("Completed", e3)));

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(
                TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(2)),
                TestDataBuilder.Group("Completed", TestDataBuilder.Entry(3))),
            [], true, SortField.LastUpdated, false, "");

        // Assert
        var all = sections.SelectMany(s => s.AllItems).ToList();
        Assert.That(all, Does.Contain(e1));
        Assert.That(all, Does.Contain(e2));
        Assert.That(all, Does.Contain(e3));
    }

    // ── Bulk-update batching (one Reset per touched section) ────────────

    [Test]
    public void Merge_BulkRemoveManyFromOneSection_FiresSingleReset()
    {
        // Five entries removed from one section should collapse into one Reset, not five.
        // Arrange
        var initial = Enumerable.Range(1, 6).Select(i => TestDataBuilder.Entry(i)).ToArray();
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", initial)));
        var watching = sections[0];

        var resets = 0;
        watching.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        // Keep only id 1; ids 2..6 removed.
        var fresh = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)));

        // Act
        MediaListSectionsMerger.Merge(sections, fresh, [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(watching.TotalCount, Is.EqualTo(1));
        Assert.That(resets, Is.EqualTo(1));
    }

    [Test]
    public void Merge_CrossSectionMoves_FireOneResetPerAffectedSection()
    {
        // Move three entries from Watching → Completed. Each side should see exactly one Reset,
        // not three (pre-batching behavior fired a Reset per RemoveItem and per AddItem).
        // Arrange
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching",
                TestDataBuilder.Entry(1), TestDataBuilder.Entry(2), TestDataBuilder.Entry(3)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(4))));
        var watching = sections[0];
        var completed = sections[1];

        var watchingResets = 0;
        var completedResets = 0;
        watching.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                watchingResets++;
            }
        };
        completed.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                completedResets++;
            }
        };

        var fresh = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", []),
            TestDataBuilder.Group("Completed",
                TestDataBuilder.Entry(1),
                TestDataBuilder.Entry(2),
                TestDataBuilder.Entry(3),
                TestDataBuilder.Entry(4)));

        // Act
        MediaListSectionsMerger.Merge(sections, fresh, [], true, SortField.LastUpdated, false, "");

        // Assert: Watching was emptied and then removed in Pass 2. Its pending bulk-update is
        // discarded before removal so it fires zero Resets — running a deferred sort/filter on
        // a section that's leaving the outer collection would be pure waste. Completed gained
        // three and keeps its reset coalesced to one.
        Assert.That(sections, Has.None.Matches<MediaListSection>(s => s is { Title: "Watching" }));
        Assert.That(sections, Has.Exactly(1).Items);
        Assert.That(completed.TotalCount, Is.EqualTo(4));
        Assert.That(watchingResets, Is.EqualTo(0));
        Assert.That(completedResets, Is.EqualTo(1));
    }

    [Test]
    public void Merge_UnchangedSectionsDuringPartialChange_FireZeroResets()
    {
        // Mutating section A must not fire any Reset on untouched section B. Regression guard
        // against scope-leak bugs where BeginBulkUpdate is called on sections that don't mutate.
        // Arrange
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(2), TestDataBuilder.Entry(3))));
        var completed = sections[1];

        var completedResets = 0;
        completed.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                completedResets++;
            }
        };

        var fresh = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1), TestDataBuilder.Entry(10)),
            TestDataBuilder.Group("Completed", TestDataBuilder.Entry(2), TestDataBuilder.Entry(3)));

        // Act
        MediaListSectionsMerger.Merge(sections, fresh, [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.That(completedResets, Is.EqualTo(0));
    }

    [Test]
    public void BulkUpdateScope_NestedScopes_OnlyOutermostDisposeTriggersReset()
    {
        // Arrange
        var section = new MediaListSection("Watching", isExpanded: true);
        section.AddItem(TestDataBuilder.Entry(1));
        var resets = 0;
        section.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        // Act
        using (var outer = section.BeginBulkUpdate())
        {
            using (var inner = section.BeginBulkUpdate())
            {
                section.AddItem(TestDataBuilder.Entry(2));
                section.AddItem(TestDataBuilder.Entry(3));
            }

            // Inner Dispose must not fire — outer scope still open.
            Assert.That(resets, Is.EqualTo(0));
            section.AddItem(TestDataBuilder.Entry(4));
        }

        // Assert: exactly one Reset fired when outer disposed.
        Assert.That(resets, Is.EqualTo(1));
        Assert.That(section.TotalCount, Is.EqualTo(4));
    }

    [Test]
    public void BulkUpdateScope_NoMutationsBetweenBeginAndEnd_FiresNoReset()
    {
        // Dirty-flag guard: opening and closing a scope with no mutations must not fire UpdateItems.
        // Arrange
        var section = new MediaListSection("Watching", isExpanded: true);
        section.AddItem(TestDataBuilder.Entry(1));
        var resets = 0;
        section.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        // Act
        using (section.BeginBulkUpdate())
        {
            // intentionally empty
        }

        // Assert
        Assert.That(resets, Is.EqualTo(0));
    }

    [Test]
    public void DiscardBulkUpdate_SuppressesDeferredUpdateItemsOnDispose()
    {
        // DiscardBulkUpdate is the merger's Pass-2 escape hatch: when a section is leaving the
        // outer collection we don't want its final sort/filter to fire. After Discard, disposing
        // the outstanding scope must NOT trigger UpdateItems, and must not leave the section in
        // a state that breaks subsequent bulk scopes.
        // Arrange
        var section = new MediaListSection("Watching", isExpanded: true);
        section.AddItem(TestDataBuilder.Entry(1));
        var resets = 0;
        section.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        // Act: open a scope, mutate (so dirty flag would fire on normal dispose), then discard.
        var scope = section.BeginBulkUpdate();
        section.AddItem(TestDataBuilder.Entry(2));
        section.DiscardBulkUpdate();
        scope.Dispose();

        // Assert: no deferred UpdateItems ran, and a fresh scope still behaves correctly.
        Assert.That(resets, Is.EqualTo(0));

        using (section.BeginBulkUpdate())
        {
            section.AddItem(TestDataBuilder.Entry(3));
        }

        Assert.That(resets, Is.EqualTo(1));
    }

    // ── Default section expanded states ─────────────────────────────────────────────

    [Test]
    public void BuildInitial_FirstSection_IsExpandedByDefault()
    {
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1))));

        Assert.That(sections, Has.Exactly(1).Items);
        Assert.That(sections[0].IsExpanded, Is.True);
    }

    [Test]
    public void BuildInitial_RewatchingSection_IsExpandedByDefault_WhenNotFirst()
    {
        // Rewatching should default to expanded even when it is not the first section.
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching",   TestDataBuilder.Entry(1)),
            TestDataBuilder.Group("Rewatching", TestDataBuilder.Entry(2)),
            TestDataBuilder.Group("Completed",  TestDataBuilder.Entry(3))));

        Assert.That(sections.Count, Is.EqualTo(3));
        Assert.That(sections[0].IsExpanded, Is.True, "Watching should be expanded (first section)");
        Assert.That(sections[1].IsExpanded, Is.True, "Rewatching should be expanded by default");
        Assert.That(sections[2].IsExpanded, Is.False, "Completed should be collapsed by default");
    }
}
