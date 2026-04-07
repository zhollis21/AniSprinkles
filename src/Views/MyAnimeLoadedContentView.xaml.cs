using System.ComponentModel;

namespace AniSprinkles.Views;

public partial class MyAnimeLoadedContentView : ContentView
{
    private readonly DataTemplate? _standardTemplate;
    private readonly DataTemplate? _largeTemplate;
    private readonly DataTemplate? _compactTemplate;
    private bool _longPressFired;

    public MyAnimeLoadedContentView()
    {
        InitializeComponent();
        _standardTemplate = (DataTemplate)Resources["StandardItemTemplate"];
        _largeTemplate = (DataTemplate)Resources["LargeItemTemplate"];
        _compactTemplate = (DataTemplate)Resources["CompactItemTemplate"];

        AnimeCollectionView.HandlerChanged += OnCollectionViewHandlerChanged;
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (_longPressFired)
        {
            _longPressFired = false;
            return;
        }

        var entry = (sender as VisualElement)?.BindingContext as MediaListEntry;
        if (entry is null || BindingContext is not MyAnimePageModel vm)
        {
            return;
        }

        _ = vm.OpenDetailsCommand.ExecuteAsync(entry);
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

    private void OnCollectionViewHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        SetupAndroidLongPress();
#endif
    }

#if ANDROID
    private void SetupAndroidLongPress()
    {
        var platformView = AnimeCollectionView.Handler?.PlatformView;

        // MauiRecyclerView extends RecyclerView, so this cast should work.
        var recyclerView = platformView as AndroidX.RecyclerView.Widget.RecyclerView;
        if (recyclerView is null)
        {
            return;
        }

        var gestureDetector = new Android.Views.GestureDetector(
            recyclerView.Context,
            new LongPressGestureListener(recyclerView, this));

        recyclerView.AddOnItemTouchListener(new RecyclerTouchListener(gestureDetector));
    }

    private MediaListEntry? GetEntryAtAdapterPosition(int adapterPosition)
    {
        if (BindingContext is not MyAnimePageModel vm)
        {
            return null;
        }

        // Grouped CollectionView layout: [Header0, Item0_0, Item0_1, ..., Header1, Item1_0, ...]
        var pos = 0;
        foreach (var section in vm.Sections)
        {
            if (pos == adapterPosition)
            {
                return null; // Section header
            }

            pos++;

            foreach (var entry in section)
            {
                if (pos == adapterPosition)
                {
                    return entry;
                }

                pos++;
            }
        }

        return null;
    }

    /// <summary>
    /// Intercepts touch events on the RecyclerView and forwards them to the GestureDetector.
    /// </summary>
    private sealed class RecyclerTouchListener : Java.Lang.Object,
        AndroidX.RecyclerView.Widget.RecyclerView.IOnItemTouchListener
    {
        private readonly Android.Views.GestureDetector _gestureDetector;

        public RecyclerTouchListener(Android.Views.GestureDetector gestureDetector)
        {
            _gestureDetector = gestureDetector;
        }

        public bool OnInterceptTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
        {
            _gestureDetector.OnTouchEvent(e);
            return false; // Don't intercept — let normal touch handling continue.
        }

        public void OnTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
        {
        }

        public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
        {
        }
    }

    /// <summary>
    /// GestureDetector listener that detects long press gestures and resolves
    /// which item was pressed via the RecyclerView's adapter position.
    /// </summary>
    private sealed class LongPressGestureListener : Android.Views.GestureDetector.SimpleOnGestureListener
    {
        private readonly AndroidX.RecyclerView.Widget.RecyclerView _recyclerView;
        private readonly MyAnimeLoadedContentView _owner;

        public LongPressGestureListener(
            AndroidX.RecyclerView.Widget.RecyclerView recyclerView,
            MyAnimeLoadedContentView owner)
        {
            _recyclerView = recyclerView;
            _owner = owner;
        }

        public override void OnLongPress(Android.Views.MotionEvent? e)
        {
            if (e is null)
            {
                return;
            }

            var childView = _recyclerView.FindChildViewUnder(e.GetX(), e.GetY());
            if (childView is null)
            {
                return;
            }

            var adapterPosition = _recyclerView.GetChildAdapterPosition(childView);
            if (adapterPosition < 0)
            {
                return;
            }

            var entry = _owner.GetEntryAtAdapterPosition(adapterPosition);
            if (entry is null || _owner.BindingContext is not MyAnimePageModel vm)
            {
                return;
            }

            _owner._longPressFired = true;
            _ = vm.ShowMoveMenuCommand.ExecuteAsync(entry);
        }
    }
#endif

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
