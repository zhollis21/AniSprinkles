namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// Route-based navigation abstraction over <c>Shell.Current</c>. Exists so PageModels
/// can be unit-tested without a live MAUI shell. Dialog/popup interactions are intentionally
/// NOT on this interface — they're UX paths not covered by PageModel state-machine tests
/// and pulling CommunityToolkit.Maui popup types into the interface would bloat it.
/// </summary>
public interface INavigationService
{
    Task GoToAsync(string route, bool animate = true, IDictionary<string, object>? parameters = null);
}
