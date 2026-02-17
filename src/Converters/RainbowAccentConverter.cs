using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

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
    /// get the same color. For example, "Current" status maps to "Watching" section.
    /// </summary>
    private static readonly Dictionary<string, string> _keyMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Current"] = "Watching",
        ["LastUpdated"] = "Updated",
        // Add more mappings as needed:
        // ["AliasKey"] = "CanonicalKey",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
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
            return Colors.Transparent;
        }

        // Apply key mapping if one exists (e.g., "Current" → "Watching")
        if (_keyMappings.TryGetValue(key, out var mappedKey))
        {
            key = mappedKey;
        }

        // Deterministic hash (stable across runs)
        var hash = StableHash(key);

        var idx = Math.Abs(hash) % _rainbowKeys.Length;
        var colorKey = _rainbowKeys[idx];

        if (Application.Current?.Resources.TryGetValue(colorKey, out var res) == true && res is Color c)
        {
            if (isTransparent)
            {
                var theme = Application.Current?.RequestedTheme ?? AppTheme.Unspecified;
                var alpha = theme == AppTheme.Dark ? 0.28f : 0.14f;

                c = c.WithAlpha(alpha);
            }

            return c;
        }

        // If Colors.xaml wasn't merged or key missing:
        return Colors.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

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