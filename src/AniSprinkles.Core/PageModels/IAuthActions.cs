using CommunityToolkit.Mvvm.Input;

namespace AniSprinkles.PageModels;

/// <summary>
/// What <c>AuthActionsView</c> binds to. That view is hosted by both Library pages
/// (<see cref="MediaListPageModel"/>) and Settings (<see cref="SettingsPageModel"/>), so its
/// <c>x:DataType</c> names this interface rather than either page model, which keeps its bindings
/// compiled (#173).
/// </summary>
public interface IAuthActions
{
    bool IsAuthenticated { get; }

    IAsyncRelayCommand SignInCommand { get; }

    IAsyncRelayCommand SignOutCommand { get; }
}
