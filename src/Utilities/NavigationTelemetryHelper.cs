using System.Collections.Generic;
using System.Linq;

namespace AniSprinkles.Utilities;

/// <summary>
/// Shared utilities for navigation telemetry and performance tracking.
/// Provides helpers for tracing navigation events and measuring tap-to-display timings.
/// </summary>
public static class NavigationTelemetryHelper
{
    /// <summary>
    /// Parses a navigation trace ID from query attributes.
    /// Used for correlating navigation events across page transitions.
    /// </summary>
    /// <param name="query">Query attributes dictionary from IQueryAttributable.ApplyQueryAttributes</param>
    /// <returns>The trace ID, or "unknown" if not found</returns>
    public static string ParseTraceId(IDictionary<string, object> query)
    {
        if (query.TryGetValue("navTraceId", out var rawTraceId) &&
            rawTraceId is string traceId &&
            !string.IsNullOrWhiteSpace(traceId))
        {
            return traceId;
        }

        return "unknown";
    }

    /// <summary>
    /// Parses the navigation start time from query attributes.
    /// Supports multiple formats: long ticks, int ticks, or string representation of long ticks.
    /// </summary>
    /// <param name="query">Query attributes dictionary from IQueryAttributable.ApplyQueryAttributes</param>
    /// <returns>The navigation start time in UTC, or null if not found/parseable</returns>
    public static DateTimeOffset? ParseNavigationStart(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("navStartUtcTicks", out var rawStart))
        {
            return null;
        }

        return rawStart switch
        {
            long ticks => new DateTimeOffset(ticks, TimeSpan.Zero),
            int ticks => new DateTimeOffset(ticks, TimeSpan.Zero),
            string text when long.TryParse(text, out var parsedTicks) => new DateTimeOffset(parsedTicks, TimeSpan.Zero),
            _ => null
        };
    }

    /// <summary>
    /// Calculates elapsed milliseconds from a navigation start time to now.
    /// Useful for performance telemetry of page transitions.
    /// </summary>
    /// <param name="navigationStartUtc">The UTC timestamp when navigation began (from user tap)</param>
    /// <returns>Elapsed milliseconds, -1 if navigationStartUtc is null, 0 as minimum</returns>
    public static long GetElapsedFromTapMilliseconds(DateTimeOffset? navigationStartUtc)
    {
        if (!navigationStartUtc.HasValue)
        {
            return -1;
        }

        return Math.Max((long)(DateTimeOffset.UtcNow - navigationStartUtc.Value).TotalMilliseconds, 0);
    }
}
