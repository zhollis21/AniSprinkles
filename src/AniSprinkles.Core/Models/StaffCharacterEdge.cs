using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public class StaffCharacterEdge : ObservableObject, IDisplayProjection
{
    public Character? Node { get; set; }
    public string? Role { get; set; }
    /// <summary>The title the card shows: the first of <see cref="AllMedia"/>, unless the adult filter picked another.</summary>
    public RelatedMedia? Media { get; set; }

    /// <summary>
    /// Every title the character appears in for this role, in AniList's order. Kept so the adult filter
    /// can show the first title that is not 18+ rather than dropping a role whose first one is (#199).
    /// </summary>
    public IReadOnlyList<RelatedMedia> AllMedia { get; init; } = [];

    /// <inheritdoc />
    /// <remarks>
    /// <c>Media</c>, not <c>Node</c>: the card shows the character's own name (which no setting
    /// affects) above the title of the media they appear in, and it is the latter that is computed
    /// from <c>AppSettings.TitleLanguage</c>.
    /// </remarks>
    public void RefreshDisplayProjections() => OnPropertyChanged(nameof(Media));
}
