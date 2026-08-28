using AniSprinkles.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Sentry;

namespace AniSprinkles.Utilities;

/// <summary>
/// Decides what happens when a reader taps a link in a character or staff bio (#137): follow it
/// in-app, explain that we can't show it, or hand it to the browser.
/// <para>
/// Lives in Core rather than beside the Android span that calls it because the decision is ordinary
/// logic and the span is not testable off-device. What stays on the platform side is only the span
/// plumbing — finding the links, painting them, and routing the touch.
/// </para>
/// </summary>
public static class BioLinkFollower
{
    /// <summary>
    /// Mirrors the toast <c>DetailsPageModelBase.NavigateToMedia</c> shows for a manga id from a
    /// relations carousel, so the answer to "can I open manga" doesn't depend on where the reader
    /// tapped. Shared so the two can't drift apart (#12).
    /// </summary>
    public const string UnsupportedMediaMessage = "Manga & Novel details aren't supported yet.";

    /// <summary>
    /// Never throws: the caller is a span click with nothing to catch for it.
    /// </summary>
    public static async Task FollowAsync(
        string? url,
        INavigationService navigation,
        IExternalBrowser browser,
        IUserFeedback feedback,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var target = AniListLinkTarget.Resolve(url);
            if (target is not null)
            {
                // A bio link is a navigation entry point that exists nowhere else in the app, so it
                // is worth tracing: "how did they reach this character page" is otherwise
                // unanswerable from a crash report.
                logger?.LogInformation(
                    "NAVTRACE BioLink → {Route} with id={EntityId}", target.Route, target.Id);
                SentrySdk.AddBreadcrumb(
                    $"Follow bio link ({target.Route} {target.Id})", "navigation", "user");

                await navigation.GoToAsync(
                    target.Route,
                    animate: false,
                    new Dictionary<string, object> { [target.ParameterName] = target.Id });
                return;
            }

            // Manga is ours, but the details page queries Media(type: ANIME) so a manga id 404s
            // there. Say what the rest of the app says rather than opening the browser.
            if (AniListLinkTarget.IsUnsupportedEntity(url))
            {
                logger?.LogInformation("NAVTRACE BioLink → skipped unsupported entity {Url}", url);
                await feedback.ShowToastAsync(UnsupportedMediaMessage);
                return;
            }

            // Staff bios link out to agency, social and personal sites; those are the browser's job.
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Host only: enough to tell an AniList entry from a social link when reading a
                // trace, without putting the full URL in the breadcrumb buffer.
                SentrySdk.AddBreadcrumb($"Open bio link externally ({uri.Host})", "navigation", "user");
                await browser.OpenAsync(uri);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to follow bio link {Url}", url);
        }
    }
}
