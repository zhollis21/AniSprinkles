using Microsoft.Extensions.Logging;

namespace AniSprinkles.Views;

/// <summary>
/// Reusable Android long-press detection for a flat (ungrouped) CollectionView — the generalized
/// form of MediaListLoadedContentView's RecyclerView listener. Resolves the pressed item by adapter
/// position from the CollectionView's ItemsSource (valid because these lists have no headers;
/// a Footer sits past the last item index and resolves to null).
///
/// MAUI's TapGestureRecognizer still fires on finger-up after a long press, so item taps must be
/// suppressed: this stamps <see cref="LongPressTapSuppressor"/>, and page-model navigate commands
/// check it before acting.
/// </summary>
public sealed class CollectionViewLongPress
{
    private readonly CollectionView _collectionView;
    private readonly Action<object> _onItemLongPressed;
#if ANDROID
    // True between long-press detection and the finger lifting. The synthetic tap fires at
    // finger-UP, which can be arbitrarily long after detection (the user can keep holding), so
    // the suppression timestamp must be re-stamped at UP — stamping only at detection would let
    // a slow release outlive the suppression window and navigate under the action sheet.
    private bool _longPressActive;
    private AndroidX.RecyclerView.Widget.RecyclerView? _attachedRecyclerView;
    private RecyclerTouchListener? _attachedTouchListener;
#endif

    private CollectionViewLongPress(CollectionView collectionView, Action<object> onItemLongPressed)
    {
        _collectionView = collectionView;
        _onItemLongPressed = onItemLongPressed;
    }

    /// <summary>
    /// Wires long-press detection to <paramref name="collectionView"/>. The helper subscribes to
    /// HandlerChanged, so it attaches/detaches with the platform view and shares the view's lifetime —
    /// callers don't need to keep the returned instance.
    /// </summary>
    public static void Attach(CollectionView collectionView, Action<object> onItemLongPressed)
    {
        var instance = new CollectionViewLongPress(collectionView, onItemLongPressed);
        collectionView.HandlerChanged += instance.OnHandlerChanged;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (_collectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
        {
            // Drop any previously-attached listener before adding a new one (handler reattachments
            // would otherwise stack listeners and fire long-press multiple times).
            Detach();

            var gestureDetector = new Android.Views.GestureDetector(
                recyclerView.Context,
                new LongPressGestureListener(recyclerView, this));
            var listener = new RecyclerTouchListener(gestureDetector, this);
            recyclerView.AddOnItemTouchListener(listener);
            _attachedRecyclerView = recyclerView;
            _attachedTouchListener = listener;
        }
        else
        {
            Detach();
        }
#endif
    }

#if ANDROID
    private void Detach()
    {
        if (_attachedRecyclerView is not null && _attachedTouchListener is not null)
        {
            _attachedRecyclerView.RemoveOnItemTouchListener(_attachedTouchListener);
        }

        _attachedTouchListener?.Dispose();
        _attachedTouchListener = null;
        _attachedRecyclerView = null;
    }

    private object? GetItemAtAdapterPosition(int adapterPosition)
    {
        if (_collectionView.ItemsSource is not System.Collections.IEnumerable source)
        {
            return null;
        }

        if (source is System.Collections.IList list)
        {
            return adapterPosition >= 0 && adapterPosition < list.Count ? list[adapterPosition] : null;
        }

        var index = 0;
        foreach (var item in source)
        {
            if (index++ == adapterPosition)
            {
                return item;
            }
        }

        return null;
    }

    private sealed class RecyclerTouchListener : Java.Lang.Object,
        AndroidX.RecyclerView.Widget.RecyclerView.IOnItemTouchListener
    {
        private readonly Android.Views.GestureDetector _gestureDetector;
        private readonly CollectionViewLongPress _owner;

        public RecyclerTouchListener(Android.Views.GestureDetector gestureDetector, CollectionViewLongPress owner)
        {
            _gestureDetector = gestureDetector;
            _owner = owner;
        }

        public bool OnInterceptTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
        {
            _gestureDetector.OnTouchEvent(e);

            // The synthetic tap fires at finger-UP; re-stamp the suppression window there so it
            // covers the release no matter how long the user kept holding after detection.
            if (_owner._longPressActive
                && e.Action is Android.Views.MotionEventActions.Up or Android.Views.MotionEventActions.Cancel)
            {
                _owner._longPressActive = false;
                LongPressTapSuppressor.Stamp();
            }

            return false; // Don't intercept — let normal touch handling continue.
        }

        public void OnTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
        {
        }

        public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
        {
        }
    }

    private sealed class LongPressGestureListener : Android.Views.GestureDetector.SimpleOnGestureListener
    {
        private readonly AndroidX.RecyclerView.Widget.RecyclerView _recyclerView;
        private readonly CollectionViewLongPress _owner;

        public LongPressGestureListener(
            AndroidX.RecyclerView.Widget.RecyclerView recyclerView,
            CollectionViewLongPress owner)
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

            var item = _owner.GetItemAtAdapterPosition(adapterPosition);
            if (item is null)
            {
                return;
            }

            // Stamp at detection too (covers a release that beats the next intercepted event),
            // then again at finger-UP via the touch listener.
            LongPressTapSuppressor.Stamp();
            _owner._longPressActive = true;
            _owner._onItemLongPressed(item);
        }
    }
#endif
}
