using System.Diagnostics;
using AniSprinkles.Services;
using AniSprinkles.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

/// <summary>
/// Runs a details-page list operation (Load More / sort change) with the shared LISTTRACE contract:
/// it times the network fetch + collection apply (so API cost is separable from the UI render that
/// follows), swallows failures so the affordance stays usable, runs the <c>onComplete</c> callback
/// to re-sync UI (e.g. the sort dropdown highlight) regardless of outcome, and on failure surfaces
/// the actionable subtitle via a snackbar.
///
/// MAUI-free (BCL + <see cref="ILogger"/> + <see cref="IUserFeedback"/> only) so it link-compiles
/// into the unit-test project and the trace/feedback contract is directly testable.
/// </summary>
public sealed class ListOperationRunner(ILogger logger, IUserFeedback feedback)
{
    /// <param name="op">Human-readable operation label for the trace (e.g. "Studio Productions · Load More").</param>
    /// <param name="entityKind">Owning entity word for the trace ("media", "staff", "character", "studio").</param>
    /// <param name="entityId">Owning entity id for the trace.</param>
    /// <param name="operation">The list mutation to run and time.</param>
    /// <param name="loadedCount">Item count after the op, for the completion trace.</param>
    /// <param name="onComplete">Optional UI re-sync, invoked after the op whether it succeeded or failed.</param>
    public async Task RunAsync(
        string op,
        string entityKind,
        int entityId,
        Func<Task> operation,
        Func<int> loadedCount,
        Action? onComplete = null)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("LISTTRACE {Op} start ({EntityKind} {EntityId})", op, entityKind, entityId);

        Exception? failure = null;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        stopwatch.Stop();
        onComplete?.Invoke();

        if (failure is null)
        {
            logger.LogInformation(
                "LISTTRACE {Op} completed in {ElapsedMs}ms ({Count} loaded); UI render follows",
                op, stopwatch.ElapsedMilliseconds, loadedCount());
            return;
        }

        logger.LogWarning(failure, "LISTTRACE {Op} failed in {ElapsedMs}ms ({EntityKind} {EntityId})", op, stopwatch.ElapsedMilliseconds, entityKind, entityId);

        // A failed sort/Load More leaves the existing list intact; surface the actionable subtitle so
        // the failure isn't silent (mirrors the chip-reverts-on-failure behavior on the detail pages).
        var message = failure is AniListApiException apiEx
            ? apiEx.UserSubtitle
            : "Couldn't update the list. Check your connection and try again.";
        await feedback.ShowSnackbarAsync(message).ConfigureAwait(true);
    }
}
