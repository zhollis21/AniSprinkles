using System.ComponentModel;

namespace AniSprinkles.Views;

public partial class DiscoverLoadedContentView : ContentView
{
    private DiscoverPageModel? _subscribedViewModel;

    public DiscoverLoadedContentView()
    {
        InitializeComponent();
        CollectionViewLongPress.Attach(SearchResultsList, item =>
        {
            if (BindingContext is DiscoverPageModel vm && vm.ShowItemActionsCommand.CanExecute(item))
            {
                vm.ShowItemActionsCommand.Execute(item);
            }
        });
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        Unsubscribe();

        if (BindingContext is DiscoverPageModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel = vm;
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null)
        {
            Unsubscribe();
        }
    }

    private void Unsubscribe()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Focus the search field the moment the bar is revealed, so the keyboard comes up ready
        // (matches the toolbar-icon reveal users expect) without a second tap.
        if (e.PropertyName == nameof(DiscoverPageModel.IsSearchVisible)
            && _subscribedViewModel?.IsSearchVisible == true)
        {
            Dispatcher.Dispatch(() => SearchEntry.Focus());
        }
    }
}
