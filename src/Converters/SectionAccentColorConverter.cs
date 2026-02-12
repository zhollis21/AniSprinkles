using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.Converters;

public sealed class RainbowAccentConverter : IValueConverter
{
    // Order matters: this is your "clock ring" sequence.
    private static readonly string[] RainbowKeys =
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

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value?.ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return Colors.Transparent;
        }

        // Deterministic hash (stable across runs)
        var hash = StableHash(key);

        var idx = Math.Abs(hash) % RainbowKeys.Length;
        var colorKey = RainbowKeys[idx];

        if (Application.Current?.Resources.TryGetValue(colorKey, out var res) == true && res is Color c)
        {
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