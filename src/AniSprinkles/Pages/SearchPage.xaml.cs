using AniSprinkles.PageModels;
using AniSprinkles.Utilities;
using AniSprinkles.Views;

namespace AniSprinkles.Pages;

/// <summary>
/// Search tab (issues #16/#43). Unlike Discover and Library there is no deferred content host:
/// the page is an Entry plus one CollectionView, light enough to build inline during the tab
/// transition.
/// </summary>
public partial class SearchPage : ContentPage
{
    private SearchPageModel? _viewModel;

    public SearchPage()
    {
        InitializeComponent();

        // Same long-press hook the Discover rows and search results used.
        CollectionViewLongPress.Attach(SearchResultsList, item =>
        {
            if (BindingContext is SearchPageModel vm && vm.ShowItemActionsCommand.CanExecute(item))
            {
                vm.ShowItemActionsCommand.Execute(item);
            }
        });
    }

    public SearchPage(SearchPageModel viewModel)
        : this()
    {
        SetViewModel(viewModel);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureViewModel();
        if (_viewModel is null)
        {
            return;
        }

        // Only steal focus when there is nothing to look at. Returning to the tab with results
        // already on screen should not throw the keyboard up over them.
        if (string.IsNullOrEmpty(_viewModel.SearchText))
        {
            Dispatcher.Dispatch(() => SearchEntry.Focus());
        }

        await _viewModel.OnAppearingAsync();
    }

    private void EnsureViewModel()
    {
        if (_viewModel is not null)
        {
            return;
        }

        try
        {
            var services = ServiceProviderHelper.GetServiceProvider();
            var viewModel = services?.GetService<SearchPageModel>();
            if (viewModel is null)
            {
                return;
            }

            SetViewModel(viewModel);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SetViewModel(SearchPageModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
    }
}
