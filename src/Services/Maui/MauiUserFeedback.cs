using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services.Maui;

/// <summary>
/// <see cref="IUserFeedback"/> adapter over CommunityToolkit.Maui's <c>Snackbar</c>/<c>Toast</c>.
/// Lives outside <c>Services.Abstractions</c> so test projects can link-compile the abstraction
/// without pulling in <c>CommunityToolkit.Maui</c>. Display failures are swallowed (and logged) so
/// a missing toast/snackbar never escalates into a crash on top of whatever the user was doing.
/// </summary>
public sealed class MauiUserFeedback(ILogger<MauiUserFeedback> logger) : IUserFeedback
{
    public async Task ShowSnackbarAsync(string message)
    {
        try
        {
            await Snackbar.Make(message, duration: TimeSpan.FromSeconds(4)).Show().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Snackbar display failed");
        }
    }

    public async Task ShowToastAsync(string message)
    {
        try
        {
            await Toast.Make(message, ToastDuration.Short).Show().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Toast display failed");
        }
    }
}
