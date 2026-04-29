using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.Utilities;

/// <summary>
/// Issue #60 workaround: after the OAuth WebView completes a successful sign-in,
/// a gray scrim appears on the page body until the user navigates to a different
/// Shell tab and back. The scrim originates below the MAUI/Android View layer
/// (HardwareRenderer / RenderNode level) and can't be cleared via the View tree.
/// Bouncing through another Shell route forces a full re-layout that clears it.
/// </summary>
public static class ShellRouteBounce
{
    /// <summary>
    /// Navigate to <paramref name="bounceRoute"/> and immediately back to
    /// <paramref name="returnRoute"/>, both with animation disabled.
    /// </summary>
    public static async Task BounceAsync(INavigationService navigationService, string bounceRoute, string returnRoute)
    {
        await navigationService.GoToAsync(bounceRoute, animate: false);
        await navigationService.GoToAsync(returnRoute, animate: false);
    }
}
