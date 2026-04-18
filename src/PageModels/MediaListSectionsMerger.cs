using System.Collections.ObjectModel;
using AniSprinkles.Models;

namespace AniSprinkles.PageModels;

/// <summary>
/// Pure in-place merge for <see cref="MediaListSection"/> collections.
/// Used by the MyAnime pull-to-refresh warm path so that unchanged data causes
/// zero CollectionView resets (and therefore zero cell re-binds / Glide decodes).
/// All inputs are passed explicitly — the merger has no dependency on AppSettings
/// so it can be exercised from pure unit tests without MAUI plumbing.
/// </summary>
public static class MediaListSectionsMerger
{
    public readonly record struct MergeResult(
        int SectionsAdded,
        int SectionsRemoved,
        int EntriesAdded,
        int EntriesRemoved,
        int EntriesMoved,
        int EntriesUpdated,
        int SectionsNeedingReset);

    /// <summary>
    /// Orders incoming groups by the user's preferred section order and drops adult entries
    /// (and any now-empty groups) when <paramref name="displayAdultContent"/> is false.
    /// Shared between the cold (BuildSections) and warm (Merge) paths so they agree on ordering.
    /// </summary>
    public static IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> OrderAndFilterGroups(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups,
        IReadOnlyList<string> sectionOrder,
        bool displayAdultContent)
    {
        IEnumerable<(string Name, IReadOnlyList<MediaListEntry> Entries)> ordered = groups;
        if (sectionOrder.Count > 0)
        {
            ordered = ordered.OrderBy(g => IndexOfOrMax(sectionOrder, g.Name));
        }

        var result = new List<(string Name, IReadOnlyList<MediaListEntry> Entries)>();
        foreach (var g in ordered)
        {
            IReadOnlyList<MediaListEntry> filtered = displayAdultContent
                ? g.Entries
                : g.Entries.Where(e => e.Media?.IsAdult != true).ToList();

            if (filtered.Count == 0)
            {
                continue;
            }

            result.Add((g.Name, filtered));
        }

        return result;
    }

    public static MergeResult Merge(
        ObservableCollection<MediaListSection> existing,
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> newGroups,
        IReadOnlyList<string> sectionOrder,
        bool displayAdultContent,
        SortField sortField,
        bool sortAscending,
        string filterText)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(newGroups);
        ArgumentNullException.ThrowIfNull(sectionOrder);

        var ordered = OrderAndFilterGroups(newGroups, sectionOrder, displayAdultContent);
        var newTitles = ordered.Select(g => g.Name).ToList();
        var newTitleSet = new HashSet<string>(newTitles, StringComparer.Ordinal);

        // Snapshot every existing entry once, keyed by MediaId. AniList invariant: a user has at most
        // one list entry per media, so cross-section duplicates shouldn't exist — but be defensive.
        var entriesById = new Dictionary<int, (MediaListEntry Entry, MediaListSection Owner)>();
        var existingMediaIds = new HashSet<int>();
        foreach (var section in existing)
        {
            foreach (var entry in section.AllItems)
            {
                if (existingMediaIds.Add(entry.MediaId))
                {
                    entriesById[entry.MediaId] = (entry, section);
                }
            }
        }

        var newMediaIds = new HashSet<int>();
        foreach (var group in ordered)
        {
            foreach (var entry in group.Entries)
            {
                newMediaIds.Add(entry.MediaId);
            }
        }

        int sectionsAdded = 0;
        int sectionsRemoved = 0;
        int entriesAdded = 0;
        int entriesMoved = 0;
        int entriesUpdated = 0;
        int sectionsNeedingReset = 0;
        var sectionsTouched = new HashSet<MediaListSection>();

        // Pass 1: process each new group — insert missing sections, diff entries against matching
        // existing sections. Cross-section moves are performed via RemoveItem + AddItem so entry
        // reference identity is preserved (guards against losing e.g. _pendingIncrementEntry).
        for (var i = 0; i < ordered.Count; i++)
        {
            var newGroup = ordered[i];
            var existingSection = FindSectionByTitle(existing, newGroup.Name);

            if (existingSection is null)
            {
                // Brand new section. Seed with new entries, reusing existing MediaListEntry references
                // where the MediaId already exists in another section (status-change move).
                var defaultExpanded = existing.Count == 0 && i == 0;
                var section = new MediaListSection(newGroup.Name, defaultExpanded);
                var seedEntries = new List<MediaListEntry>(newGroup.Entries.Count);

                foreach (var e in newGroup.Entries)
                {
                    if (entriesById.TryGetValue(e.MediaId, out var match))
                    {
                        match.Owner.RemoveItem(match.Entry);
                        sectionsTouched.Add(match.Owner);
                        UpdateInPlace(match.Entry, e);
                        seedEntries.Add(match.Entry);
                        entriesMoved++;
                        entriesUpdated++;
                        entriesById[e.MediaId] = (match.Entry, section);
                    }
                    else
                    {
                        seedEntries.Add(e);
                        entriesAdded++;
                        entriesById[e.MediaId] = (e, section);
                    }
                }

                section.AddItems(seedEntries);
                section.ApplySort(sortField, sortAscending);
                if (!string.IsNullOrWhiteSpace(filterText))
                {
                    section.ApplyFilter(filterText);
                }

                // Append at the end; final ReorderToMatch step puts every section in the correct slot.
                existing.Add(section);
                sectionsAdded++;
                continue;
            }

            // Existing section. Drop entries whose MediaId is not in the new group (either gone
            // entirely or moved to a different section — Pass 2 will handle the move).
            var newIds = new HashSet<int>(newGroup.Entries.Select(e => e.MediaId));
            var toRemove = existingSection.AllItems.Where(e => !newIds.Contains(e.MediaId)).ToList();
            foreach (var entry in toRemove)
            {
                existingSection.RemoveItem(entry);
                sectionsTouched.Add(existingSection);
            }

            var mediaChangedInSection = false;
            foreach (var newEntry in newGroup.Entries)
            {
                if (entriesById.TryGetValue(newEntry.MediaId, out var match))
                {
                    if (!ReferenceEquals(match.Owner, existingSection))
                    {
                        match.Owner.RemoveItem(match.Entry);
                        existingSection.AddItem(match.Entry);
                        sectionsTouched.Add(match.Owner);
                        sectionsTouched.Add(existingSection);
                        entriesMoved++;
                    }

                    if (MediaDisplayChanged(match.Entry.Media, newEntry.Media))
                    {
                        mediaChangedInSection = true;
                    }

                    // When a filter is active, MediaListSection.MatchesFilter searches all three title
                    // fields (English/Romaji/Native) regardless of which one DisplayTitle resolves to.
                    // MediaDisplayChanged only compares DisplayTitle, so a non-displayed-language title
                    // change wouldn't re-evaluate filter membership. Mark the section touched so
                    // ApplyFilter re-runs in Pass 3.
                    if (!string.IsNullOrWhiteSpace(filterText)
                        && FilterRelevantTitleChanged(match.Entry.Media?.Title, newEntry.Media?.Title))
                    {
                        sectionsTouched.Add(existingSection);
                    }

                    // The currently-active sort key is about to be overwritten by UpdateInPlace.
                    // If it changed, the section needs a re-sort even when no structural or
                    // MediaDisplayChanged trigger fired. Scoping to the active field avoids needless
                    // Reset events (e.g. Title-sort + progress bump shouldn't re-sort). Title is
                    // already covered by MediaDisplayChanged via Media.DisplayTitle.
                    var sortKeyChanged = sortField switch
                    {
                        SortField.LastUpdated => match.Entry.UpdatedAt != newEntry.UpdatedAt,
                        SortField.Score => match.Entry.Score != newEntry.Score,
                        SortField.AverageScore =>
                            match.Entry.Media?.AverageScore != newEntry.Media?.AverageScore,
                        _ => false,
                    };
                    if (sortKeyChanged)
                    {
                        sectionsTouched.Add(existingSection);
                    }

                    UpdateInPlace(match.Entry, newEntry);
                    entriesUpdated++;
                    entriesById[newEntry.MediaId] = (match.Entry, existingSection);
                }
                else
                {
                    existingSection.AddItem(newEntry);
                    sectionsTouched.Add(existingSection);
                    entriesById[newEntry.MediaId] = (newEntry, existingSection);
                    entriesAdded++;
                }
            }

            if (mediaChangedInSection)
            {
                sectionsTouched.Add(existingSection);
                sectionsNeedingReset++;
            }
        }

        // Pass 2: remove sections that are no longer in the new set, or that ended up empty.
        foreach (var section in existing.ToList())
        {
            if (!newTitleSet.Contains(section.Title) || section.TotalCount == 0)
            {
                existing.Remove(section);
                sectionsTouched.Remove(section);
                sectionsRemoved++;
            }
        }

        // Count missing MediaIds as removals (post-merge view).
        var entriesRemoved = 0;
        foreach (var id in existingMediaIds)
        {
            if (!newMediaIds.Contains(id))
            {
                entriesRemoved++;
            }
        }

        // Pass 3: re-sort sections that had structural changes, media-display changes, or a
        // sort-key change on any entry. Sections where only Status or Progress changed are
        // skipped — those aren't sort keys and the [ObservableProperty] notifications on
        // MediaListEntry already refreshed bound labels without forcing a CollectionView reset.
        foreach (var section in sectionsTouched)
        {
            section.ApplySort(sortField, sortAscending);
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                section.ApplyFilter(filterText);
            }
        }

        // Pass 4: reorder the outer collection so sections match the user's preferred order.
        ReorderToMatch(existing, newTitles);

        return new MergeResult(
            sectionsAdded,
            sectionsRemoved,
            entriesAdded,
            entriesRemoved,
            entriesMoved,
            entriesUpdated,
            sectionsNeedingReset);
    }

    /// <summary>
    /// Copies mutable fields from <paramref name="updated"/> onto <paramref name="existing"/>.
    /// The three [ObservableProperty] setters (Status, Progress, Score) fire PropertyChanged
    /// when the value actually changes, refreshing bound labels without a cell rebind.
    /// </summary>
    public static void UpdateInPlace(MediaListEntry existing, MediaListEntry updated)
    {
        existing.Status = updated.Status;
        existing.Progress = updated.Progress;
        existing.Score = updated.Score;

        existing.Id = updated.Id;
        existing.Repeat = updated.Repeat;
        existing.Notes = updated.Notes;
        existing.Private = updated.Private;
        existing.HiddenFromStatusLists = updated.HiddenFromStatusLists;
        existing.UpdatedAt = updated.UpdatedAt;
        existing.Media = updated.Media;
    }

    /// <summary>
    /// Returns true when Media fields referenced directly by a list-cell binding have changed.
    /// These bindings don't listen to PropertyChanged (Media itself is a plain property), so a
    /// section-level Reset is the only way to refresh them.
    /// </summary>
    public static bool MediaDisplayChanged(Media? old, Media? @new)
    {
        if (ReferenceEquals(old, @new))
        {
            return false;
        }

        if (old is null || @new is null)
        {
            return true;
        }

        if (old.CoverImage?.Medium != @new.CoverImage?.Medium
            || old.CoverImage?.Large != @new.CoverImage?.Large)
        {
            return true;
        }

        if (old.Episodes != @new.Episodes)
        {
            return true;
        }

        if (!string.Equals(old.DisplayTitle, @new.DisplayTitle, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(old.Format, @new.Format, StringComparison.Ordinal))
        {
            return true;
        }

        var oldAiring = old.NextAiringEpisode;
        var newAiring = @new.NextAiringEpisode;
        if (oldAiring?.Episode != newAiring?.Episode
            || oldAiring?.AiringAt != newAiring?.AiringAt)
        {
            return true;
        }

        return false;
    }

    private static bool FilterRelevantTitleChanged(MediaTitle? old, MediaTitle? @new)
    {
        if (ReferenceEquals(old, @new))
        {
            return false;
        }

        return !string.Equals(old?.English, @new?.English, StringComparison.Ordinal)
            || !string.Equals(old?.Romaji, @new?.Romaji, StringComparison.Ordinal)
            || !string.Equals(old?.Native, @new?.Native, StringComparison.Ordinal);
    }

    private static MediaListSection? FindSectionByTitle(
        ObservableCollection<MediaListSection> sections,
        string title)
    {
        foreach (var section in sections)
        {
            if (string.Equals(section.Title, title, StringComparison.Ordinal))
            {
                return section;
            }
        }

        return null;
    }

    private static void ReorderToMatch(
        ObservableCollection<MediaListSection> sections,
        IReadOnlyList<string> desiredOrder)
    {
        for (var targetIdx = 0; targetIdx < desiredOrder.Count; targetIdx++)
        {
            var desiredTitle = desiredOrder[targetIdx];
            var currentIdx = IndexOfTitle(sections, desiredTitle);
            if (currentIdx < 0 || currentIdx == targetIdx)
            {
                continue;
            }

            sections.Move(currentIdx, targetIdx);
        }
    }

    private static int IndexOfTitle(ObservableCollection<MediaListSection> sections, string title)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (string.Equals(sections[i].Title, title, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfOrMax(IReadOnlyList<string> order, string title)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], title, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
