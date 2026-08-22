using AniSprinkles.Models;
using AniSprinkles.Services;
using AniSprinkles.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

/// <summary>
/// Shared optimistic favorite-toggle for the details pages: flips <see cref="IFavouritable.IsFavourite"/>
/// and bumps <see cref="IFavouritable.Favourites"/> ±1 immediately, fires the AniList
/// <c>ToggleFavourite</c> mutation, and reverts both with a retry snackbar on failure. An in-flight
/// guard (<see cref="IsBusy"/>) makes rapid taps no-ops so we never double-fire or corrupt the count.
/// The caller passes an <c>onChanged</c> callback to raise its property notifications (heart fill,
/// count text, command CanExecute) after each state change.
///
/// MAUI-free (BCL + <see cref="ILogger"/> + <see cref="IUserFeedback"/> only) so it link-compiles into
/// the unit-test project and the optimistic/rollback contract is directly testable.
/// </summary>
public sealed class FavouriteToggleRunner(IAniListClient client, IUserFeedback feedback, ILogger logger)
{
    private bool _busy;

    /// <summary>True while a toggle is in flight; callers gate their command CanExecute on this.</summary>
    public bool IsBusy => _busy;

    /// <param name="entity">The entity to toggle; mutated in place.</param>
    /// <param name="kind">Which AniList favorite field to flip.</param>
    /// <param name="onChanged">Raises the caller's property/CanExecute notifications after each change.</param>
    /// <param name="retry">Re-invokes the toggle from the failure snackbar's action button.</param>
    /// <returns>True when the mutation succeeded; false if it was skipped (busy) or failed.</returns>
    public async Task<bool> ToggleAsync(IFavouritable entity, FavouriteKind kind, Action onChanged, Action retry)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        onChanged(); // disable the affordance while in flight

        var previousIsFavourite = entity.IsFavourite;
        var previousFavourites = entity.Favourites;

        // Optimistic: flip the heart and bump the visible count immediately.
        entity.IsFavourite = !previousIsFavourite;
        entity.Favourites = Math.Max(0, (entity.Favourites ?? 0) + (entity.IsFavourite ? 1 : -1));
        onChanged();

        Exception? failure = null;
        try
        {
            await client.ToggleFavouriteAsync(kind, entity.Id).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = ex;
            logger.LogError(ex, "Failed to toggle favourite for {Kind} {Id}.", kind, entity.Id);

            // Roll the heart and count back together.
            entity.IsFavourite = previousIsFavourite;
            entity.Favourites = previousFavourites;
        }

        // Clear busy (and re-enable the affordance) BEFORE the snackbar so its Retry action can
        // immediately fire another toggle instead of being swallowed by the in-flight guard.
        _busy = false;
        onChanged();

        if (failure is null)
        {
            return true;
        }

        await feedback.ShowSnackbarAsync(
            "Failed to update favorite. Please try again.",
            "Retry",
            retry).ConfigureAwait(true);
        return false;
    }
}
