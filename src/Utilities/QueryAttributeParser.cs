namespace AniSprinkles.Utilities;

/// <summary>
/// Helpers for reading <see cref="Microsoft.Maui.Controls.IQueryAttributable"/> values. Shell may
/// deliver a route parameter either as its original type (e.g. <see cref="int"/>) or as a string
/// (deep links, serialized navigation), so the detail pages all need the same int-or-string parse.
/// </summary>
public static class QueryAttributeParser
{
    /// <summary>Reads an int route parameter, accepting either an <see cref="int"/> or a numeric string. Returns 0 if absent/unparseable.</summary>
    public static int ParseInt(IDictionary<string, object> query, string key)
    {
        if (query.TryGetValue(key, out var raw))
        {
            if (raw is int id)
            {
                return id;
            }

            if (raw is string text && int.TryParse(text, out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }
}
