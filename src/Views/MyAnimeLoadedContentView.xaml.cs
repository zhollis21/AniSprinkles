using System.ComponentModel;

namespace AniSprinkles.Views;

public partial class MyAnimeLoadedContentView : ContentView
{
    private DataTemplate? _standardTemplate;
    private DataTemplate? _largeTemplate;
    private DataTemplate? _compactTemplate;

    public MyAnimeLoadedContentView()
    {
        InitializeComponent();
        _standardTemplate = (DataTemplate)Resources["StandardItemTemplate"];
        _largeTemplate = (DataTemplate)Resources["LargeItemTemplate"];
        _compactTemplate = (DataTemplate)Resources["CompactItemTemplate"];
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is MyAnimePageModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyViewMode(vm.CurrentViewMode);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MyAnimePageModel.CurrentViewMode) && sender is MyAnimePageModel vm)
        {
            ApplyViewMode(vm.CurrentViewMode);
        }
    }

    private void ApplyViewMode(ListViewMode mode)
    {
        var cv = AnimeCollectionView;
        if (cv is null)
        {
            return;
        }

        cv.ItemTemplate = mode switch
        {
            ListViewMode.Large => _largeTemplate,
            ListViewMode.Compact => _compactTemplate,
            _ => _standardTemplate
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

        // MeasureFirstItem caches the previous template's item size, causing wrong
        // heights after a template switch. Use MeasureAllItems to force remeasure.
        cv.ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems;
    }
}
