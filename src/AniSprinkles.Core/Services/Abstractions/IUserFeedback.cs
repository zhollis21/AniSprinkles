namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// Transient, non-blocking user feedback (snackbars / toasts). Abstracted so PageModels and the
/// shared <see cref="AniSprinkles.PageModels.ListOperationRunner"/> can be unit-tested without
/// CommunityToolkit.Maui's MAUI-only <c>Snackbar</c>/<c>Toast</c> statics (which don't resolve on
/// the plain <c>net10.0</c> test TFM). Mirrors why <see cref="INavigationService"/> exists.
/// </summary>
public interface IUserFeedback
{
    /// <summary>Shows a brief snackbar with the given message. Never throws — display failures are logged.</summary>
    Task ShowSnackbarAsync(string message);

    /// <summary>
    /// Shows a snackbar with an action button (e.g. "Retry") that invokes <paramref name="action"/>
    /// when tapped. Never throws — display failures are logged.
    /// </summary>
    /// <param name="duration">
    /// Overrides the default dwell time. Settings' save-failure snackbar holds for 20 seconds
    /// because the user has to notice it to retry; everything else uses the default.
    /// </param>
    Task ShowSnackbarAsync(string message, string actionText, Action action, TimeSpan? duration = null);

    /// <summary>Shows a short toast with the given message. Never throws — display failures are logged.</summary>
    Task ShowToastAsync(string message);
}
