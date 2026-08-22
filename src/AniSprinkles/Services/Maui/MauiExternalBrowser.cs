using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services.Maui;

/// <summary>
/// <see cref="IExternalBrowser"/> adapter over MAUI Essentials' <c>Browser.Default</c>. Kept out of
/// Core because <c>OpenAsync</c> throws <c>NotImplementedInReferenceAssemblyException</c> on the
/// plain <c>net10.0</c> TFM the unit tests run on. Mirrors <see cref="MauiUserFeedback"/>.
/// </summary>
public sealed class MauiExternalBrowser(ILogger<MauiExternalBrowser> logger) : IExternalBrowser
{
    public async Task OpenAsync(Uri uri)
    {
        try
        {
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            // Failing to hand off to a browser is never worth crashing over — the call sites are all
            // optional "view this on AniList" affordances.
            logger.LogWarning(ex, "Failed to open {Uri} in the system browser.", uri);
        }
    }
}
