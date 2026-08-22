using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services.Maui;

/// <summary>
/// <see cref="INavigationService"/> adapter that forwards to <c>Shell.Current.GoToAsync</c>.
/// Lives outside <c>Services.Abstractions</c> so test projects can link-compile the abstraction
/// without also pulling in a reference to <c>Microsoft.Maui.Controls.Shell</c>.
/// </summary>
public sealed class MauiShellNavigationService(ILogger<MauiShellNavigationService> logger) : INavigationService
{
    public Task GoToAsync(string route, bool animate = true, IDictionary<string, object>? parameters = null)
    {
        if (Shell.Current is null)
        {
            // Shell can legitimately be null during early startup/teardown, so we stay forgiving
            // rather than throwing — but surface a warning so a wiring bug doesn't vanish silently.
            logger.LogWarning("Navigation to '{Route}' skipped: Shell.Current is null.", route);
            return Task.CompletedTask;
        }

        return parameters is null
            ? Shell.Current.GoToAsync(route, animate)
            : Shell.Current.GoToAsync(route, animate, parameters);
    }
}
