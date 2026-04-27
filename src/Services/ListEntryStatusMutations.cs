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
                if (entry.HasKnownEpisodeCount && entry.MaxEpisodes is { } max)
                {
                    entry.Progress = max;
                }

                return true;

            case MediaListStatus.Repeating:
                entry.Status = MediaListStatus.Repeating;
                entry.Progress = 0;
                entry.Repeat = (entry.Repeat ?? 0) + 1;
                return false;

            default:
                entry.Status = target;
                return false;
        }
    }
}
