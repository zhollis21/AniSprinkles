namespace AniSprinkles.Models;

/// <summary>
/// AniList's <c>MediaRelation</c> enum as asked for at <c>relationType(version: 3)</c>. Parsed from
/// the raw value rather than from <see cref="MediaRelationEdge.RelationType"/>, which is the
/// title-cased display string ("Side Story") and not something to branch on (#103).
/// </summary>
public enum MediaRelationKind
{
    /// <summary>Absent, or a value this build does not know.</summary>
    Unknown,
    Adaptation,
    Prequel,
    Sequel,
    Parent,
    SideStory,
    Character,
    Summary,
    Alternative,
    SpinOff,
    Other,
    Source,
    Compilation,
    Contains,
    SameUniverse,
}

public static class MediaRelationKindExtensions
{
    /// <summary>
    /// <c>SIDE_STORY</c> → <see cref="MediaRelationKind.SideStory"/>. Anything unrecognised is
    /// <see cref="MediaRelationKind.Unknown"/>, so a relation type AniList adds later is simply never
    /// offered rather than misread as one we act on.
    /// </summary>
    public static MediaRelationKind ParseMediaRelationKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return MediaRelationKind.Unknown;
        }

        var name = raw.Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<MediaRelationKind>(name, ignoreCase: true, out var kind)
               && Enum.IsDefined(kind)
               && !int.TryParse(name, out _)
            ? kind
            : MediaRelationKind.Unknown;
    }
}
