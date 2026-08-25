using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public class StaffCharacterEdge : ObservableObject, IDisplayProjection
{
    public Character? Node { get; set; }
    public string? Role { get; set; }
    public RelatedMedia? Media { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// <c>Media</c>, not <c>Node</c>: the card shows the character's own name (which no setting
    /// affects) above the title of the media they appear in, and it is the latter that is computed
    /// from <c>AppSettings.TitleLanguage</c>.
    /// </remarks>
    public void RefreshDisplayProjections() => OnPropertyChanged(nameof(Media));
}
