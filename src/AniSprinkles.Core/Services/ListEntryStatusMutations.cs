using AniSprinkles.Models;

namespace AniSprinkles.Services;

/// <summary>
/// The pure (UI-free) half of <see cref="ListEntryStatusFlow"/>: applies the
/// side effects of a list-entry status change to the entry in place. Exists as
/// its own file so it can be link-compiled into the unit-test project without
/// dragging in MAUI popup dependencies.
/// </summary>
public static class ListEntryStatusMutations
{
    /// <summary>
    /// Applies the non-UI status mutation and returns <c>true</c> when the caller
    /// should follow up with a score prompt (Completed target). Callers are
    /// responsible for persisting the entry and for showing any popups.
    /// </summary>
    public static bool ApplyStatusChange(MediaListEntry entry, MediaListStatus target)
    {
        ArgumentNullException.ThrowIfNull(entry);

        switch (target)
        {
            case MediaListStatus.Completed:
                entry.Status = MediaListStatus.Completed;
                if (entry.HasKnownProgressTotal && entry.ActiveProgressTotal is { } max)
                {
                    entry.SetActiveProgress(max);
                }

                return true;

            case MediaListStatus.Repeating:
                entry.Status = MediaListStatus.Repeating;
                // BOTH counters, not just the active one. A reread starts from zero either way, and
                // clearing only chapters on an entry that tracks both would leave volumes above zero
                // with chapters at zero — which is exactly the shape MediaListEntry.UsesVolumeProgress
                // reads as "this reader tracks volumes", flipping a freshly reset entry to show a
                // full volume count (#12).
                entry.Progress = 0;
                entry.ProgressVolumes = 0;
                entry.Repeat = (entry.Repeat ?? 0) + 1;
                return false;

            case MediaListStatus.Current:
                entry.Status = MediaListStatus.Current;
                // Moving an at-cap entry (typically Completed → Watching) back into
                // Watching with progress already at the maximum would leave the +1
                // button dead and violate the "Watching never sits at max" invariant
                // enforced by the +1 → Complete prompt. Walk progress back by one so
                // there's at least one episode left to watch. HasKnownProgressTotal
                // gates this on a declared total — currently-airing shows whose cap
                // is the last-aired episode are left untouched (the user is just
                // caught up, not finished), as are still-publishing manga, which have
                // no declared total at all.
                if (entry.HasKnownProgressTotal
                    && entry.ActiveProgressTotal is { } currentMax
                    && entry.ActiveProgress == currentMax)
                {
                    entry.SetActiveProgress(currentMax - 1);
                }
                return false;

            default:
                entry.Status = target;
                return false;
        }
    }
}
