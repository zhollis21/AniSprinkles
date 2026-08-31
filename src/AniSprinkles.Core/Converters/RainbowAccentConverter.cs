using System.Globalization;

namespace AniSprinkles.Converters;

public sealed class RainbowAccentConverter : IValueConverter
{
    // Order matters: this is your "clock ring" sequence.
    private static readonly string[] _rainbowKeys =
    [
        "RainbowRed",
        "RainbowOrange",
        "RainbowYellow",
        "RainbowGreen",
        "RainbowCyan",
        "RainbowBlue",
        "RainbowPurple",
        "RainbowPink",
    ];

    /// <summary>
    /// Maps certain keys to other keys before hashing, ensuring related concepts
    /// get the same color. For example, "Current" status maps to "Watching" section,
    /// and "Repeating" (the AniList section-order key) maps to "Rewatching" (the display name).
    /// </summary>
    private static readonly Dictionary<string, string> _keyMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Current"] = "Watching",
        ["Repeating"] = "Rewatching",
        ["LastUpdated"] = "Updated",
        // Add more mappings as needed:
        // ["AliasKey"] = "CanonicalKey",
    };

    /// <summary>
    /// Hardcoded colors for the keys that must not be left to the hash: the standard list status
    /// sections (anime and manga alike), and Settings' two stat cards. Checked after
    /// <see cref="_keyMappings"/> are applied, before the hash fallback, so each of these always
    /// renders with a distinct, consistent color rather than whatever its name happens to hash to.
    /// </summary>
    private static readonly Dictionary<string, string> _fixedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Watching"]   = "RainbowBlue",
        ["Rewatching"] = "RainbowCyan",
        // The manga list’s own section names (#12). Same colours as their anime counterparts on
        // purpose — without these they would miss the table and fall through to the hash palette,
        // so the two halves of the Library tab would not agree on what "currently reading" looks like.
        ["Reading"]    = "RainbowBlue",
        ["Rereading"]  = "RainbowCyan",
        ["Planning"]   = "RainbowPurple",
        ["Completed"]  = "RainbowGreen",
        ["Paused"]     = "RainbowYellow",
        ["Dropped"]    = "RainbowRed",

        // Settings' two stat cards (#12). These keys exist only there, deliberately: the obvious
        // parameters — "Manga", "Chapters", "Volumes" — are already spoken for by Search's type
        // pills, the Library nav titles and the details-page chips, so pinning those to suit a
        // stat tile would silently recolour four other screens.
        //
        // Pinned rather than hashed because the two cards have to stay visually apart, and the
        // hash does not care: left to it, Manga, Chapters and Volumes all landed on RainbowBlue,
        // the same colour as Mean Score, so every manga figure rendered identically blue beside a
        // four-colour anime card.
        //
        // The palette has eight stops and the two cards have ten slots, so exactly two colours
        // recur. Both are deliberate: RainbowBlue on each Mean Score, which is the same metric on
        // both cards, and RainbowCyan on the anime header and the manga Volumes tile, which never
        // sit in the same role. The anime entries pin the colours that card already rendered, so
        // it does not change.
        ["StatsAnimeHeader"] = "RainbowCyan",
        ["StatsTotalAnime"]  = "RainbowPink",
        ["StatsEpisodes"]    = "RainbowGreen",
        ["StatsDaysWatched"] = "RainbowYellow",
        ["StatsAnimeScore"]  = "RainbowBlue",

        ["StatsMangaHeader"] = "RainbowPurple",
        ["StatsTotalManga"]  = "RainbowRed",
        ["StatsChapters"]    = "RainbowOrange",
        ["StatsVolumes"]     = "RainbowCyan",
        ["StatsMangaScore"]  = "RainbowBlue",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? key;

        var isParameterBool = bool.TryParse(parameter?.ToString(), out var isTransparent);
        if (parameter == null || isParameterBool)
        {
            key = value?.ToString();
        }
        else // If the parameter isn't a bool, lets treat it as the key
        {
            key = parameter?.ToString();
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return ToTarget(Colors.Transparent, targetType);
        }

        var colorKey = ResourceKeyFor(key);

        if (Application.Current?.Resources.TryGetValue(colorKey, out var res) == true && res is Color c)
        {
            if (isTransparent)
            {
                c = c.WithAlpha(0.28f);
            }

            return ToTarget(c, targetType);
        }

        // If Colors.xaml wasn't merged or key missing:
        return ToTarget(Colors.Transparent, targetType);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// The palette resource key a given name maps to. Public because not every caller can go through
    /// an <see cref="IValueConverter"/>: the bio-link spans (#137) are built on the Android side and
    /// colour themselves per link, and they need this exact hash rather than a second one that would
    /// drift — a character's name should paint the same colour wherever it appears.
    /// </summary>
    public static string ResourceKeyFor(string key)
    {
        // Apply key mapping if one exists (e.g., "Current" → "Watching", "Repeating" → "Rewatching")
        if (_keyMappings.TryGetValue(key, out var mappedKey))
        {
            key = mappedKey;
        }

        // Use hardcoded color for known status sections and the stat cards; fall back to hash for everything else.
        if (_fixedColors.TryGetValue(key, out var colorKey))
        {
            return colorKey;
        }

        // Deterministic hash (stable across runs).
        // Special-case int.MinValue: Math.Abs(int.MinValue) overflows and throws.
        var hash = StableHash(key);
        var idx = (hash == int.MinValue ? 0 : Math.Abs(hash)) % _rainbowKeys.Length;
        return _rainbowKeys[idx];
    }

    // Borders carry an implicit style that sets Background (a Brush), and MAUI's Background always
    // wins over BackgroundColor — so a Border accent must bind Background, not BackgroundColor. When
    // the binding target is a Brush, hand back a SolidColorBrush; otherwise (TextColor etc.) a Color.
    private static object ToTarget(Color color, Type targetType)
        => typeof(Brush).IsAssignableFrom(targetType) ? new SolidColorBrush(color) : color;

    private static int StableHash(string s)
    {
        // FNV-1a 32-bit (fast, deterministic)
        unchecked
        {
            const int fnvOffset = (int)2166136261;
            const int fnvPrime = 16777619;

            int hash = fnvOffset;
            foreach (var ch in s)
            {
                hash ^= ch;
                hash *= fnvPrime;
            }

            return hash;
        }
    }
}