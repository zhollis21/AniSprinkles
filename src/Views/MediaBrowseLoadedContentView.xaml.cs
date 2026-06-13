using System.ComponentModel;

namespace AniSprinkles.Views;

public partial class MediaBrowseLoadedContentView : ContentView
{
    private MediaBrowsePageModel? _subscribedViewModel;

    public MediaBrowseLoadedContentView()
    {
        InitializeComponent();
        CollectionViewLongPress.Attach(BrowseList, item =>
        {
            if (BindingContext is MediaBrowsePageModel vm && vm.ShowItemActionsCommand.CanExecute(item))
            {
                vm.ShowItemActionsCommand.Execute(item);
            }
        });
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        Unsubscribe();

        if (BindingContext is MediaBrowsePageModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel = vm;
            ApplyViewMode(vm.CurrentViewMode);
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
        if (e.PropertyName == nameof(MediaBrowsePageModel.CurrentViewMode) && sender is MediaBrowsePageModel vm)
        {
            ApplyViewMode(vm.CurrentViewMode);
        }
    }

    // Mirrors MyAnimeLoadedContentView.ApplyViewMode; the templates live app-wide in
    // BrowseTemplates.xaml (merged via App.xaml) because the search results list shares them.
    private void ApplyViewMode(ListViewMode mode)
    {
        var cv = BrowseList;
        if (cv is null)
        {
            return;
        }

        cv.ItemTemplate = mode switch
        {
            ListViewMode.Large => FindAppTemplate("BrowseMediaLargeTemplate"),
            ListViewMode.Compact => FindAppTemplate("BrowseMediaCompactTemplate"),
            _ => FindAppTemplate("BrowseMediaRowTemplate"),
        };

        cv.ItemsLayout = mode switch
        {
            ListViewMode.Large => new GridItemsLayout(2, ItemsLayoutOrientation.Vertical)
            {
                VerticalItemSpacing = 4,
                HorizontalItemSpacing = 4
            },
            _ => new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
            {
                ItemSpacing = 0
            }
        };

        cv.ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems;
    }

    private static DataTemplate? FindAppTemplate(string key)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true
            ? value as DataTemplate
            : null;
}
