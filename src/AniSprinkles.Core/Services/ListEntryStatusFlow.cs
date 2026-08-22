namespace AniSprinkles.Services;

/// <summary>
/// Shared helper that applies the side effects of a list-entry status change and,
/// where appropriate, prompts the user for confirmation or a rating. Used by both
/// the My Anime and Details pages so the two pages behave identically when
/// completing, rewatching, or switching status.
///
/// All methods mutate the passed <see cref="MediaListEntry"/> in place. Callers
/// should always persist the entry via the AniList client after
/// <see cref="ApplyStatusChangeAsync(MediaListEntry, MediaListStatus)"/>, and persist
/// after <see cref="ApplyCompletionAsync(MediaListEntry)"/> only when that method
/// returns <c>true</c> (it returns <c>false</c> when the user cancels). Callers are
/// responsible for reverting their own optimistic UI on failure.
///
/// Instance rather than static since #62: the confirm and rating prompts go through
/// <see cref="IDialogService"/> so this type can live in Core and be unit-tested.
/// Registered as a singleton; it holds no state.
/// </summary>
public sealed class ListEntryStatusFlow(IDialogService dialogs)
{
    /// <summary>
    /// Applies <paramref name="target"/> to <paramref name="entry"/> along with any
    /// status-specific side effects (progress, repeat, score prompt). The score
    /// prompt is optional (skipping it preserves the existing score but does not
    /// cancel the status change), so the caller should always proceed to save.
    /// </summary>
    public async Task ApplyStatusChangeAsync(MediaListEntry entry, MediaListStatus target)
    {
        var needsScorePrompt = ListEntryStatusMutations.ApplyStatusChange(entry, target);
        if (needsScorePrompt)
        {
            var score = await dialogs.ShowRatingAsync(entry.Media?.DisplayTitle, entry.Score);
            if (score.HasValue)
            {
                entry.Score = score.Value;
            }
        }
    }

    /// <summary>
    /// Invoked when the user has just incremented progress to the known total episode
    /// count. Shows the confirmation popup and — if confirmed — sets progress to max,
    /// status to Completed, and prompts for a score (pre-populated from the entry's
    /// existing score). Returns <c>true</c> when the caller should save.
    ///
    /// Must only be called for entries with <see cref="MediaListEntry.HasKnownEpisodeCount"/>.
    /// Long-running airing shows without a finite total should not route through here.
    /// </summary>
    public async Task<bool> ApplyCompletionAsync(MediaListEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.HasKnownEpisodeCount || entry.MaxEpisodes is not { } total || entry.Media is null)
        {
            return false;
        }

        var confirmed = await dialogs.ConfirmAsync(
            title: "All episodes watched!",
            message: $"You've watched all {total} episodes of {entry.Media.DisplayTitle}. Mark as Completed?",
            confirmText: "Yes",
            cancelText: "No",
            iconGlyph: Glyphs.Regular.CheckmarkCircle24);

        if (!confirmed)
        {
            return false;
        }

        SentrySdk.AddBreadcrumb($"Completion confirmed (entry {entry.Id})", "list", "user");

        entry.Progress = total;
        entry.Status = MediaListStatus.Completed;

        var score = await dialogs.ShowRatingAsync(entry.Media.DisplayTitle, entry.Score);
        if (score.HasValue)
        {
            entry.Score = score.Value;
        }

        return true;
    }

    /// <summary>
    /// Prompts for a score (pre-populated from <paramref name="entry"/>'s existing score) and applies
    /// it to the entry. Returns <c>true</c> only when the user chose a score, so the caller knows
    /// whether to persist; skipping or dismissing the popup leaves the score unchanged and returns
    /// <c>false</c>.
    /// </summary>
    public async Task<bool> ApplyRatingAsync(MediaListEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var score = await dialogs.ShowRatingAsync(entry.Media?.DisplayTitle, entry.Score);
        if (score.HasValue)
        {
            entry.Score = score.Value;
            return true;
        }

        return false;
    }
}
