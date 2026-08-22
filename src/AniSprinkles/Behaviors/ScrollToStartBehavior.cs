namespace AniSprinkles.Behaviors;

/// <summary>
/// Scrolls the attached <see cref="CollectionView"/> back to the front whenever <see cref="Trigger"/>
/// changes (after its initial value). The details-page carousels bind <see cref="Trigger"/> to their
/// active-sort property so a sort change always returns the list to index 0 — see #105.
///
/// Why a behavior keyed on the sort value rather than scrolling from the sort dropdown or the page
/// model: the three sort-apply paths mutate their bound collection differently. The server-refetch
/// path (<c>PaginatedSection.ChangeSortAsync</c>) clears the list across an await, so the underlying
/// RecyclerView already resets to 0; the synchronous local-reorder fast path
/// (<c>ApplyLocalSort</c>) and Relations (<c>ApplyRelationsSort</c>) rebuild in one frame and keep
/// the old offset. Keying off the bound sort value gives one consistent reset across all three, fires
/// only when a sort actually commits (the property doesn't change on a failed server sort), and stays
/// out of the MAUI-free page-model/section layer.
/// </summary>
public sealed class ScrollToStartBehavior : Behavior<CollectionView>
{
    private CollectionView? _collectionView;

    // The first Trigger assignment is the initial sort on page load — binding it must not scroll.
    private bool _hasInitialValue;

    public static readonly BindableProperty TriggerProperty =
        BindableProperty.Create(nameof(Trigger), typeof(object), typeof(ScrollToStartBehavior), null, propertyChanged: OnTriggerChanged);

    /// <summary>Bind to the carousel's active-sort value; a change scrolls the list to the front.</summary>
    public object? Trigger
    {
        get => GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    protected override void OnAttachedTo(CollectionView bindable)
    {
        base.OnAttachedTo(bindable);
        _collectionView = bindable;
        // Behaviors don't inherit BindingContext automatically; propagate it (and keep it in sync) so
        // the {Binding} on Trigger resolves against the page model.
        BindingContext = bindable.BindingContext;
        bindable.BindingContextChanged += OnElementBindingContextChanged;
    }

    protected override void OnDetachingFrom(CollectionView bindable)
    {
        bindable.BindingContextChanged -= OnElementBindingContextChanged;
        _collectionView = null;
        // Drop the page-model reference and reset the first-value flag so a later re-attach starts
        // clean (the re-bound initial sort is treated as initial again, not as a change that scrolls).
        BindingContext = null;
        _hasInitialValue = false;
        base.OnDetachingFrom(bindable);
    }

    private void OnElementBindingContextChanged(object? sender, EventArgs e)
        => BindingContext = _collectionView?.BindingContext;

    private static void OnTriggerChanged(BindableObject bindable, object oldValue, object newValue)
        => ((ScrollToStartBehavior)bindable).OnTriggerChanged();

    private void OnTriggerChanged()
    {
        if (!_hasInitialValue)
        {
            _hasInitialValue = true;
            return;
        }

        var collectionView = _collectionView;
        if (collectionView is null)
        {
            return;
        }

        // Dispatch so the scroll runs after the synchronous collection rebuild and the current layout
        // pass — this also removes any dependence on whether the sort property is notified before or
        // after the items change (Relations notifies before rebuilding).
        collectionView.Dispatcher.Dispatch(() =>
        {
            // ScrollTo by index on an empty list can throw; only reset when there's something to show.
            // The carousels bind ObservableCollection, so a non-generic ICollection.Count check is
            // allocation-free and avoids enumerating (and disposing) an enumerator.
            if (collectionView.ItemsSource is System.Collections.ICollection { Count: > 0 })
            {
                collectionView.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            }
        });
    }
}
