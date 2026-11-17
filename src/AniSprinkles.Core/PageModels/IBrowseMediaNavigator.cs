using AniSprinkles.Models;
using CommunityToolkit.Mvvm.Input;

namespace AniSprinkles.PageModels;

/// <summary>
/// What the row templates in <c>BrowseTemplates.xaml</c> bind their tap to. Those templates are
/// shared by Search (<see cref="SearchPageModel"/>) and the browse lists
/// (<see cref="MediaBrowsePageModel"/>), so they find the page model with
/// <c>RelativeSource AncestorType</c> on this interface rather than on either type, which keeps the
/// binding compiled (#173).
/// </summary>
public interface IBrowseMediaNavigator
{
    IAsyncRelayCommand<BrowseMediaItem?> NavigateToMediaCommand { get; }
}
