using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AniSprinkles.Utilities;

namespace AniSprinkles.UnitTests;

public class MediaListSectionsMergerTests
{
    public MediaListSectionsMergerTests() => TestDataBuilder.ResetAppSettings();

    // ── Empty/populated permutations ────────────────────────────────────

    [Fact]
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
        Assert.Empty(sections);
        Assert.Equal(0, result.SectionsAdded);
        Assert.Equal(0, result.EntriesAdded);
    }

    [Fact]
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
        Assert.Equal(2, sections.Count);
        Assert.Equal("Watching", sections[0].Title);
        Assert.Equal("Completed", sections[1].Title);
        Assert.Equal(2, sections[0].TotalCount);
        Assert.Equal(1, sections[1].TotalCount);
        Assert.Equal(2, result.SectionsAdded);
        Assert.Equal(3, result.EntriesAdded);
    }

    [Fact]
    public void Merge_PopulatedToEmpty_RemovesAllSections()
    {
        // Arrange
        var sections = TestDataBuilder.BuildInitial(TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1))));

        // Act
        var result = MediaListSectionsMerger.Merge(
            sections, TestDataBuilder.Groups(), [], true, SortField.LastUpdated, false, "");

        // Assert
        Assert.Empty(sections);
        Assert.Equal(1, result.SectionsRemoved);
        Assert.Equal(1, result.EntriesRemoved);
    }

    [Fact]
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
        Assert.Equal(0, outerChanges);
        Assert.Equal(0, sectionResets);
    }

    // ── Entry-level diffs (single section) ──────────────────────────────

    [Fact]
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
        Assert.Same(watching, sections[0]);
        Assert.Equal(2, watching.TotalCount);
        Assert.Equal(1, result.EntriesAdded);
    }

    [Fact]
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
        Assert.Equal(1, sections[0].TotalCount);
        Assert.Equal(1, sections[0].AllItems[0].MediaId);  // id 2 was the one removed
        Assert.Equal(1, result.EntriesRemoved);
    }

    [Fact]
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
        Assert.Same(original, watching.AllItems[0]);  // reference preserved
        Assert.Equal(4, original.Progress);
        Assert.True(progressChanged);
        Assert.False(sectionReset);
    }

    [Fact]
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
        Assert.Equal(9.0, original.Score);
        Assert.True(scoreChanged);
    }

    [Fact]
    public void Merge_UpdatedAtChange_WithLastUpdatedSort_ReOrdersSection()
    {
        // Arrange — entry 1 updated later than entry 2; descending LastUpdated sort puts 1 first.
        var e1 = TestDataBuilder.Entry(1, updatedAt: DateTimeOffset.FromUnixTimeSeconds(200));
        var e2 = TestDataBuilder.Entry(2, updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));
        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1, e2)),
            sortField: SortField.LastUpdated,
            sortAscending: false);
        Assert.Equal(1, sections[0][0].MediaId); // pre-condition

        // Now entry 2 gets bumped to a newer UpdatedAt than entry 1.
        var e1Same = TestDataBuilder.Entry(1, updatedAt: DateTimeOffset.FromUnixTimeSeconds(200));
        var e2Bumped = TestDataBuilder.Entry(2, updatedAt: DateTimeOffset.FromUnixTimeSeconds(300));

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1Same, e2Bumped)),
            [], true, SortField.LastUpdated, false, "");

        // Assert — entry 2 now sorts to the top of the visible, sorted collection.
        Assert.Equal(2, sections[0][0].MediaId);
        Assert.Equal(1, sections[0][1].MediaId);
    }

    [Fact]
    public void Merge_ScoreChange_WithScoreSort_ReOrdersSection()
    {
        // Arrange — sorted descending by Score, entry 1 (9.0) above entry 2 (5.0).
        var e1 = TestDataBuilder.Entry(1, score: 9.0);
        var e2 = TestDataBuilder.Entry(2, score: 5.0);
        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1, e2)),
            sortField: SortField.Score,
            sortAscending: false);
        Assert.Equal(1, sections[0][0].MediaId); // pre-condition

        // Entry 2's score is raised above entry 1's.
        var e1Same = TestDataBuilder.Entry(1, score: 9.0);
        var e2Bumped = TestDataBuilder.Entry(2, score: 10.0);

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1Same, e2Bumped)),
            [], true, SortField.Score, false, "");

        // Assert
        Assert.Equal(2, sections[0][0].MediaId);
    }

    [Fact]
    public void Merge_AverageScoreChange_WithAverageScoreSort_ReOrdersSection()
    {
        // Arrange — sorted descending by Media.AverageScore, entry 1 (90) above entry 2 (70).
        var e1 = TestDataBuilder.Entry(1);
        e1.Media!.AverageScore = 90;
        var e2 = TestDataBuilder.Entry(2);
        e2.Media!.AverageScore = 70;

        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1, e2)),
            sortField: SortField.AverageScore,
            sortAscending: false);
        Assert.Equal(1, sections[0][0].MediaId); // pre-condition

        // Entry 2's average is raised above entry 1's.
        var e1Same = TestDataBuilder.Entry(1);
        e1Same.Media!.AverageScore = 90;
        var e2Bumped = TestDataBuilder.Entry(2);
        e2Bumped.Media!.AverageScore = 95;

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching", e1Same, e2Bumped)),
            [], true, SortField.AverageScore, false, "");

        // Assert
        Assert.Equal(2, sections[0][0].MediaId);
    }

    [Fact]
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
        Assert.Equal(0, sectionResets);
    }

    [Fact]
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
        Assert.Same(original, sections[0].AllItems[0]);
        Assert.Equal("https://img/new", original.Media?.CoverImage?.Medium);
        Assert.True(sectionReset);
        Assert.Equal(1, result.SectionsNeedingReset);
    }

    [Fact]
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
        Assert.Equal(1, result.SectionsNeedingReset);
    }

    // ── Section-level diffs ─────────────────────────────────────────────

    [Fact]
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
        Assert.Equal(3, initial.Count);
        Assert.Equal("Watching", initial[0].Title);
        Assert.Equal("Paused", initial[1].Title);
        Assert.Equal("Completed", initial[2].Title);
    }

    [Fact]
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
        Assert.Single(initial);
        Assert.Equal("Watching", initial[0].Title);
    }

    [Fact]
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
        Assert.Same(completed, sections[0]);
        Assert.Same(watching, sections[1]);
        Assert.True(moves >= 1);
        Assert.Equal(0, otherStructuralChanges);
    }

    [Fact]
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
        Assert.False(sections[0].IsExpanded);
    }

    // ── Cross-section moves ─────────────────────────────────────────────

    [Fact]
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
        Assert.Same(original, movedEntry);  // entry instance preserved across sections
        Assert.Equal(MediaListStatus.Completed, original.Status);
        Assert.DoesNotContain(sections, s => s.Title == "Watching");
    }

    [Fact]
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
        Assert.Single(watching.AllItems);
        Assert.Same(c, watching.AllItems[0]);
        Assert.Equal(2, completed.AllItems.Count);
        Assert.Contains(a, completed.AllItems);
        Assert.Contains(b, completed.AllItems);
    }

    // ── Filter / sort interaction ────────────────────────────────────────

    [Fact]
    public void Merge_WithActiveFilter_FilterReappliedToTouchedSections()
    {
        // Arrange
        var sections = TestDataBuilder.BuildInitial(
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching",
                TestDataBuilder.Entry(1, title: "Apple"),
                TestDataBuilder.Entry(2, title: "Banana"))),
            filterText: "ban");

        Assert.Single(sections[0]);  // pre-condition: only Banana visible under active filter

        // Act
        MediaListSectionsMerger.Merge(
            sections,
            TestDataBuilder.Groups(TestDataBuilder.Group("Watching",
                TestDataBuilder.Entry(1, title: "Apple"),
                TestDataBuilder.Entry(2, title: "Banana"),
                TestDataBuilder.Entry(3, title: "Banshee"))),
            [], true, SortField.Title, true, "ban");

        // Assert
        Assert.Equal(3, sections[0].TotalCount);
        Assert.Equal(2, sections[0].Count);  // visible: Banana + Banshee
    }

    // ── Adult content / reorder helpers ──────────────────────────────────

    [Fact]
    public void OrderAndFilterGroups_DropsAdultEntriesWhenDisabled()
    {
        // Arrange
        var adult = TestDataBuilder.Entry(1, isAdult: true);
        var safe = TestDataBuilder.Entry(2, isAdult: false);
        var groups = TestDataBuilder.Groups(TestDataBuilder.Group("Watching", adult, safe));

        // Act
        var filtered = MediaListSectionsMerger.OrderAndFilterGroups(groups, [], displayAdultContent: false);

        // Assert
        Assert.Single(filtered);
        Assert.Single(filtered[0].Entries);
        Assert.Equal(2, filtered[0].Entries[0].MediaId);
    }

    [Fact]
    public void OrderAndFilterGroups_DropsNowEmptySections_WhenAdultFilterEmptiesThem()
    {
        // Arrange
        var groups = TestDataBuilder.Groups(
            TestDataBuilder.Group("Watching", TestDataBuilder.Entry(1, isAdult: true)));

        // Act
        var filtered = MediaListSectionsMerger.OrderAndFilterGroups(groups, [], displayAdultContent: false);

        // Assert
        Assert.Empty(filtered);
    }

    [Fact]
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
        Assert.Equal(["Watching", "Completed", "CustomList"], ordered.Select(g => g.Name));
    }

    // ── MediaDisplayChanged primitive ────────────────────────────────────

    [Fact]
    public void MediaDisplayChanged_BothNull_ReturnsFalse()
    {
        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(null, null);

        // Assert
        Assert.False(changed);
    }

    [Fact]
    public void MediaDisplayChanged_OneNull_ReturnsTrue()
    {
        // Act & Assert (two symmetric cases; see also the [Theory] refactor opportunity)
        Assert.True(MediaListSectionsMerger.MediaDisplayChanged(null, new Media()));
        Assert.True(MediaListSectionsMerger.MediaDisplayChanged(new Media(), null));
    }

    [Fact]
    public void MediaDisplayChanged_SameCoverAndAiring_ReturnsFalse()
    {
        // Arrange
        var a = new Media { CoverImage = new MediaCoverImage { Medium = "u" }, Episodes = 12 };
        var b = new Media { CoverImage = new MediaCoverImage { Medium = "u" }, Episodes = 12 };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.False(changed);
    }

    [Fact]
    public void MediaDisplayChanged_EpisodesDifferent_ReturnsTrue()
    {
        // Arrange
        var a = new Media { Episodes = 12 };
        var b = new Media { Episodes = 13 };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public void MediaDisplayChanged_FormatDifferent_ReturnsTrue()
    {
        // Arrange — list cell binds Media.Format via FormatIconBadge, so a
        // change must trigger a section Reset to refresh the badge.
        var a = new Media { Format = "TV_SHORT" };
        var b = new Media { Format = "TV" };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public void MediaDisplayChanged_SameFormat_ReturnsFalse()
    {
        // Arrange
        var a = new Media { Format = "TV" };
        var b = new Media { Format = "TV" };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.False(changed);
    }

    [Fact]
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
        Assert.True(changed);
    }

    [Fact]
    public void MediaDisplayChanged_SameDisplayTitle_ReturnsFalse()
    {
        // Arrange
        var a = new Media { Title = new MediaTitle { Romaji = "Same" } };
        var b = new Media { Title = new MediaTitle { Romaji = "Same" } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.False(changed);
    }

    [Fact]
    public void MediaDisplayChanged_CoverLargeDifferent_ReturnsTrue()
    {
        // Arrange — MediaDetails opens a larger cover; MyAnime grid layout binds .Large too.
        var a = new Media { CoverImage = new MediaCoverImage { Large = "https://img/old" } };
        var b = new Media { CoverImage = new MediaCoverImage { Large = "https://img/new" } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public void MediaDisplayChanged_NextAiringEpisodeChanged_ReturnsTrue()
    {
        // Arrange
        var a = new Media { NextAiringEpisode = new MediaAiringEpisode { Episode = 5, AiringAt = 1000 } };
        var b = new Media { NextAiringEpisode = new MediaAiringEpisode { Episode = 6, AiringAt = 2000 } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.True(changed);
    }

    [Fact]
    public void MediaDisplayChanged_SameNextAiringEpisode_ReturnsFalse()
    {
        // Arrange
        var a = new Media { NextAiringEpisode = new MediaAiringEpisode { Episode = 5, AiringAt = 1000 } };
        var b = new Media { NextAiringEpisode = new MediaAiringEpisode { Episode = 5, AiringAt = 1000 } };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.False(changed);
    }

    [Fact]
    public void MediaDisplayChanged_SameEpisodes_ReturnsFalse()
    {
        // Arrange — negative complement to MediaDisplayChanged_EpisodesDifferent_ReturnsTrue.
        var a = new Media { Episodes = 12 };
        var b = new Media { Episodes = 12 };

        // Act
        var changed = MediaListSectionsMerger.MediaDisplayChanged(a, b);

        // Assert
        Assert.False(changed);
    }

    // ── Reference preservation regression guard ──────────────────────────

    [Fact]
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
        Assert.Contains(e1, all);
        Assert.Contains(e2, all);
        Assert.Contains(e3, all);
    }
}
