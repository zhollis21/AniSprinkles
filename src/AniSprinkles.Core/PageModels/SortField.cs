namespace AniSprinkles.PageModels;

/// <summary>
/// Fields available for sorting media list entries within each section.
/// </summary>
public enum SortField
{
    /// <summary>Sort by the date the entry was last updated on AniList.</summary>
    LastUpdated,

    /// <summary>Sort alphabetically by the display title.</summary>
    Title,

    /// <summary>Sort by the user's score.</summary>
    Score,

    /// <summary>Sort by the community average score.</summary>
    AverageScore
}
