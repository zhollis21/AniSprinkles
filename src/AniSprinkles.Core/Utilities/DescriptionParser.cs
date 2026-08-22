using System.Text;
using System.Text.RegularExpressions;

namespace AniSprinkles.Utilities;

/// <summary>
/// AniList character descriptions follow a convention: one or more lines of
/// <c>__Label:__ value</c> markdown stat rows at the top, optionally wrapped in
/// <c>~!...!~</c> spoiler markers, followed by a blank line, then the prose biography.
/// This parser splits that structure so the UI can render stats as a key-value card
/// and prose as a separate Read-more card. Staff descriptions are usually pure prose;
/// the parser falls through to an empty Stats list and the full input as Prose.
/// </summary>
public static class DescriptionParser
{
    // Label has no colons (the colon ends the label) and no underscores (which would
    // collide with the bold markers). Everything between matching <code>__</code> bold pairs.
    private static readonly Regex StatLineRegex = new(
        @"^__(?<label>[^_:]+):__\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled);

    public static ParsedDescription Parse(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return ParsedDescription.Empty;
        }

        var stats = new List<DescriptionStatRow>();
        var proseBuilder = new StringBuilder();
        var foundProse = false;

        foreach (var rawLine in description.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                if (foundProse)
                {
                    proseBuilder.AppendLine();
                }
                continue;
            }

            DescriptionStatRow? stat = null;
            if (!foundProse)
            {
                stat = TryParseStatLine(line);
            }

            if (stat is not null)
            {
                stats.Add(stat);
            }
            else
            {
                foundProse = true;
                proseBuilder.AppendLine(rawLine);
            }
        }

        return new ParsedDescription
        {
            Stats = stats,
            Prose = proseBuilder.ToString().Trim(),
        };
    }

    private static DescriptionStatRow? TryParseStatLine(string line)
    {
        var isRowSpoiler = false;
        if (line.StartsWith("~!", StringComparison.Ordinal) && line.EndsWith("!~", StringComparison.Ordinal))
        {
            isRowSpoiler = true;
            line = line[2..^2].Trim();
        }

        var match = StatLineRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var label = match.Groups["label"].Value.Trim();
        var value = match.Groups["value"].Value.Trim();
        if (label.Length == 0)
        {
            return null;
        }

        // Detect inline spoiler wrapping the entire value (label visible, value hidden).
        var isValueSpoiler = false;
        if (!isRowSpoiler
            && value.StartsWith("~!", StringComparison.Ordinal)
            && value.EndsWith("!~", StringComparison.Ordinal))
        {
            isValueSpoiler = true;
            value = value[2..^2].Trim();
        }

        return new DescriptionStatRow
        {
            Label = label,
            Value = value,
            IsRowSpoiler = isRowSpoiler,
            IsValueSpoiler = isValueSpoiler,
        };
    }
}

public sealed class ParsedDescription
{
    public static ParsedDescription Empty { get; } = new();

    public IReadOnlyList<DescriptionStatRow> Stats { get; init; } = [];
    public string Prose { get; init; } = string.Empty;
}

public sealed class DescriptionStatRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public bool IsRowSpoiler { get; init; }
    public bool IsValueSpoiler { get; init; }
}
