namespace AniSprinkles.PageModels;

/// <summary>
/// The canonical Library sort rows, shared by both halves of the tab as <c>(Code, Display)</c> pairs, where <c>Code</c> is "Field:dir"
/// (Field a <see cref="SortField"/> name, dir "asc"/"desc"). Kept as pure data — no MAUI dependency — so
/// it can be link-compiled into the unit tests, which assert every Code parses. That test is a build-time
/// guard backing the runtime tripwire in <see cref="AnimeLibraryPageModel.SelectSort"/>: the picker should only
/// ever emit valid codes, and this keeps that true. <see cref="AnimeLibraryPageModel.SortOptions"/> wraps these
/// into fresh <see cref="SortOption"/> objects so the mutable IsSelected highlight is never shared.
/// </summary>
public static class MediaListSortDefinitions
{
    public static readonly IReadOnlyList<(string Code, string Display)> All =
    [
        ("LastUpdated:desc",  "Recently Updated"),
        ("LastUpdated:asc",   "Oldest Updated"),
        ("Title:asc",         "Title (A→Z)"),
        ("Title:desc",        "Title (Z→A)"),
        ("Score:desc",        "My Score (high→low)"),
        ("Score:asc",         "My Score (low→high)"),
        ("AverageScore:desc", "Avg Score (high→low)"),
        ("AverageScore:asc",  "Avg Score (low→high)"),
    ];
}
