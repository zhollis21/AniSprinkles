using AniSprinkles.PageModels;
using AniSprinkles.Utilities;

namespace AniSprinkles.Pages;

/// <summary>
/// Search tab (issues #16/#43). Unlike Discover and Library there is no deferred content host:
/// the page is an Entry plus one CollectionView, light enough to build inline during the tab
/// transition.
/// </summary>
public partial class SearchPage : ContentPage, IScrollsToTop
{
    private SearchPageModel? _viewModel;

    public SearchPage()
    {
        InitializeComponent();
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

    /// <summary>
    /// Re-tapping the Search tab at its root (#200): back to the first result and into the search box,
    /// ready for a new query. The query itself is kept (the page model is a singleton on purpose).
    /// Instant scroll for the same reason as the Library list: results page in, so the list can be long.
    /// </summary>
    public void ScrollToTop()
    {
        if (SearchResultsList.IsVisible
            && SearchResultsList.ItemsSource is System.Collections.ICollection { Count: > 0 })
        {
            SearchResultsList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
        }

        SearchEntry.Focus();
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
