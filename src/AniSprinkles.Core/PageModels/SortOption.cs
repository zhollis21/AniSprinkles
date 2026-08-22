namespace AniSprinkles.PageModels;

/// <summary>
/// One row in a section's sort dropdown. <see cref="Code"/> is what we wire to the API
/// (e.g. AniList's MediaSort/StaffSort/CharacterSort enum string), <see cref="Display"/>
/// is the human label.
/// </summary>
public sealed partial class SortOption : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string Code { get; init; }
    public required string Display { get; init; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isSelected;
}
