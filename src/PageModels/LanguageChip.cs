using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.PageModels;

public partial class LanguageChip : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public string Display { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
