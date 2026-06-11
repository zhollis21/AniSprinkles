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

    /// <summary>Shows a short toast with the given message. Never throws — display failures are logged.</summary>
    Task ShowToastAsync(string message);
}
