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
    //
    // Both spellings of the separator are accepted, because AniList editors write both:
    // <c>__Height:__ 172 cm</c> and <c>__Height__: 145-180 cm</c>. Only the first used to parse,
    // and since a miss on the opening line latches the parser into prose for the rest of the
    // description, a bio starting with the second form lost its whole stat card — later rows in
    // the valid spelling included.
    private static readonly Regex StatLineRegex = new(
        @"^__(?<label>[^_:]+)(?::__|__\s*:)\s*(?<value>.+?)\s*$",
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
        var inSpoilerBlock = false;

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
                stat = TryParseStatLine(line, ref inSpoilerBlock);
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

    /// <param name="inSpoilerBlock">
    /// Carries "we are inside a <c>~!…!~</c> block that opened on an earlier line" across lines.
    /// AniList wraps several consecutive stat rows in one block rather than marking each row, and
    /// handling only the single-line form meant the opening line failed to parse — which ended the
    /// stats section early and dropped every row after it, spoiler or not, into the prose card.
    /// </param>
    private static DescriptionStatRow? TryParseStatLine(string line, ref bool inSpoilerBlock)
    {
        var isRowSpoiler = inSpoilerBlock;

        if (inSpoilerBlock)
        {
            if (line.EndsWith("!~", StringComparison.Ordinal))
            {
                inSpoilerBlock = false;
                line = line[..^2].TrimEnd();
            }
        }
        else if (line.StartsWith("~!", StringComparison.Ordinal))
        {
            isRowSpoiler = true;

            // Length guard: "~!" on its own would make the range below throw.
            if (line.Length >= 4 && line.EndsWith("!~", StringComparison.Ordinal))
            {
                line = line[2..^2].Trim();
            }
            else
            {
                inSpoilerBlock = true;
                line = line[2..].TrimStart();
            }
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
