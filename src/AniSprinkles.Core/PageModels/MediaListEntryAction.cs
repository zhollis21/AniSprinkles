namespace AniSprinkles.PageModels;

/// <summary>
/// The per-entry action a user picked from the long-press menu. The menu popup is a pure router —
/// each value maps to an existing dedicated flow in <see cref="EntryActionCoordinator"/>.
/// </summary>
public enum MediaListEntryAction
{
    OpenDetails,
    EditProgress,
    MarkCompleted,
    Rate,
    MoveToList,
    Remove,
}
