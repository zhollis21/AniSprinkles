using System.ComponentModel;
using System.Runtime.CompilerServices;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Views;

public partial class MediaListLoadedContentView : ContentView
{
    private readonly DataTemplate? _standardTemplate;
    private readonly DataTemplate? _largeTemplate;
    private readonly DataTemplate? _compactTemplate;
    private readonly ILogger<MediaListLoadedContentView>? _logger;
    private readonly int _viewId;
    private MediaListPageModel? _subscribedViewModel;

    public MediaListLoadedContentView()
    {
        InitializeComponent();
        _standardTemplate = (DataTemplate)Resources["StandardItemTemplate"];
        _largeTemplate = (DataTemplate)Resources["LargeItemTemplate"];
        _compactTemplate = (DataTemplate)Resources["CompactItemTemplate"];

        _viewId = RuntimeHelpers.GetHashCode(this);
        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<MediaListLoadedContentView>();
        }
        catch (InvalidOperationException)
        {
            // DI not ready; logging is optional instrumentation.
        }

        _logger?.LogInformation("LOADEDVIEW MediaList[#{ViewId:X}] constructed", _viewId);

        AnimeCollectionView.HandlerChanged += OnCollectionViewHandlerChanged;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        _logger?.LogInformation(
            "LOADEDVIEW MediaList[#{ViewId:X}] OnHandlerChanged (handler={HasHandler})",
            _viewId, Handler is not null);

        // When the view is detached, release the PropertyChanged subscription so the singleton
        // MediaListPageModel stops pinning this (now-orphan) view.
        if (Handler is null)
        {
            UnsubscribeFromViewModel();
        }
    }

    private void UnsubscribeFromViewModel()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        var entry = (sender as VisualElement)?.BindingContext as MediaListEntry;
        if (entry is null || BindingContext is not MediaListPageModel vm)
        {
            return;
        }

        _ = vm.OpenDetailsCommand.ExecuteAsync(entry);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        // Drop the previous VM's subscription before wiring a new one. Without this, swapping
        // BindingContext leaves a dangling event handler that keeps the old view alive for the
        // lifetime of the singleton VM.
        UnsubscribeFromViewModel();

        if (BindingContext is MediaListPageModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel = vm;
            ApplyViewMode(vm.CurrentViewMode);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaListPageModel.CurrentViewMode) && sender is MediaListPageModel vm)
        {
            ApplyViewMode(vm.CurrentViewMode);
        }
    }

    private void OnCollectionViewHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        // MauiRecyclerView extends RecyclerView.
        if (AnimeCollectionView.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
        {
            _logger?.LogInformation(
                "LOADEDVIEW MediaList[#{ViewId:X}] RecyclerView handler change (platformView=null).",
                _viewId);
            return;
        }

        // Capture the RecyclerView's Context identity. This is the Android FragmentActivity
        // that Glide captures when binding images in each cell. If the hash ever differs from
        // the current MainActivity hash (see LIFECYCLE logs), we've captured a destroyed activity.
        var ctx = recyclerView.Context;
        _logger?.LogInformation(
            "LOADEDVIEW MediaList[#{ViewId:X}] RecyclerView handler attached (contextType={ContextType}, contextHash=#{ContextHash:X})",
            _viewId,
            ctx?.GetType().Name ?? "null",
            ctx is null ? 0 : ctx.GetHashCode());
#endif
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

        cv.ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems;
    }
}
