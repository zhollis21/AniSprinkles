using System.Globalization;

namespace AniSprinkles.Models;

public class StatusDistribution
{
    public string? Status { get; set; }
    public int? Amount { get; set; }

    public string StatusDisplay => string.IsNullOrWhiteSpace(Status)
        ? ""
        : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Status.ToLowerInvariant().Replace('_', ' '));
}
