using AniSprinkles.Models;
using AniSprinkles.Views;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace AniSprinkles.Services;

/// <summary>
/// Shared helper that applies the side effects of a list-entry status change and,
/// where appropriate, prompts the user for confirmation or a rating. Used by both
/// the My Anime and Details pages so the two pages behave identically when
/// completing, rewatching, or switching status.
///
/// All methods mutate the passed <see cref="MediaListEntry"/> in place. Callers are
/// responsible for persisting the entry via the AniList client when the method
/// returns <c>true</c>, and for reverting their own optimistic UI on failure.
/// </summary>
public static class ListEntryStatusFlow
{
    private static readonly PopupOptions TransparentPopupOptions = new()
    {
        Shape = null,
        Shadow = null,
        CanBeDismissedByTappingOutsideOfPopup = false,
    };

    /// <summary>
    /// Applies <paramref name="target"/> to <paramref name="entry"/> along with any
    /// status-specific side effects (progress, repeat, score prompt). The score
    /// prompt is optional (skipping it preserves the existing score but does not
    /// cancel the status change), so the caller should always proceed to save.
    /// </summary>
    public static async Task ApplyStatusChangeAsync(MediaListEntry entry, MediaListStatus target)
    {
        var needsScorePrompt = ListEntryStatusMutations.ApplyStatusChange(entry, target);
        if (needsScorePrompt)
        {
            var score = await PromptForScoreAsync(entry.Media?.DisplayTitle, entry.Score);
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
    public static async Task<bool> ApplyCompletionAsync(MediaListEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.HasKnownEpisodeCount || entry.MaxEpisodes is not { } total || entry.Media is null)
        {
            return false;
        }

        var confirmed = await ShowCompletionPopupAsync(entry.Media.DisplayTitle, total);
        if (!confirmed)
        {
            return false;
        }

        entry.Progress = total;
        entry.Status = MediaListStatus.Completed;

        var score = await PromptForScoreAsync(entry.Media.DisplayTitle, entry.Score);
        if (score.HasValue)
        {
            entry.Score = score.Value;
        }

        return true;
    }

    private static async Task<bool> ShowCompletionPopupAsync(string animeTitle, int totalEpisodes)
    {
        if (Shell.Current?.CurrentPage is not { } page)
        {
            return false;
        }

        var popup = new CompletionPopup(animeTitle, totalEpisodes);
        var result = await page.ShowPopupAsync<bool>(popup, TransparentPopupOptions, CancellationToken.None);
        return !result.WasDismissedByTappingOutsideOfPopup && result.Result;
    }

    private static async Task<double?> PromptForScoreAsync(string? animeTitle, double? initialScore)
    {
        if (Shell.Current?.CurrentPage is not { } page)
        {
            return null;
        }

        var popup = new RatingPopup(animeTitle, initialScore);
        var result = await page.ShowPopupAsync<object>(popup, TransparentPopupOptions, CancellationToken.None);
        if (result.WasDismissedByTappingOutsideOfPopup)
        {
            return null;
        }

        return result.Result as double?;
    }
}
