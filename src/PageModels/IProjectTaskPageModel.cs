using AniSprinkles.Models;
using CommunityToolkit.Mvvm.Input;

namespace AniSprinkles.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}