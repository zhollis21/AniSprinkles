using System.Globalization;

namespace AniSprinkles.Utilities;

/// <summary>
/// Formats count-style metrics (favourites, popularity, recommendation rating) for compact display on
/// list cards: empty for null/non-positive, "1.2M" once it reaches a million, "1.2k" once it reaches
/// 1000, plain otherwise.
/// </summary>
public static class MetricFormat
{
    public static string Compact(int? value) => value switch
    {
        null or <= 0 => string.Empty,
        // 999,950+ promotes early: the k tier's one-decimal rounding would render it as "1000k".
        >= 999_950 => (value.Value / 1_000_000.0).ToString("0.#M", CultureInfo.InvariantCulture),
        >= 1000 => (value.Value / 1000.0).ToString("0.#k", CultureInfo.InvariantCulture),
        _ => value.Value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Like <see cref="Compact"/> but renders missing/non-positive values as "0" rather than
    /// blank — for always-on metric badges where an empty value would read as broken.</summary>
    public static string CompactOrZero(int? value) => value is > 0 ? Compact(value) : "0";
}
