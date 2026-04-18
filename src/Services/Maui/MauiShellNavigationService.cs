using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.Services.Maui;

/// <summary>
/// <see cref="INavigationService"/> adapter that forwards to <c>Shell.Current.GoToAsync</c>.
/// Lives outside <see cref="Abstractions"/> so test projects can link-compile the abstraction
/// without also pulling in a reference to <c>Microsoft.Maui.Controls.Shell</c>.
/// </summary>
public sealed class MauiShellNavigationService : INavigationService
{
    public Task GoToAsync(string route, bool animate = true, IDictionary<string, object>? parameters = null)
    {
        if (Shell.Current is null)
        {
            return Task.CompletedTask;
        }

        return parameters is null
            ? Shell.Current.GoToAsync(route, animate)
            : Shell.Current.GoToAsync(route, animate, parameters);
    }
}
