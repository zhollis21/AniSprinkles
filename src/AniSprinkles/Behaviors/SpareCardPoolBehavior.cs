namespace AniSprinkles.Behaviors;

/// <summary>
/// Lets the attached list keep more spare cards for reuse, so a fast fling stops throwing cards away
/// and rebuilding them (#205).
/// <para>
/// Android's RecyclerView puts each card that scrolls off screen onto a shelf of spares, and the next
/// card that scrolls on takes one and rebinds it. The shelf holds 5 cards per view type by default.
/// A fling in the 2-column Large grid detaches more than that at once, so the extras are dropped and
/// built again a moment later, and the dropped ones wait in both runtimes' heaps until a .NET and a
/// Java collection have run. With 100 Watching entries on a Pixel 10 Pro XL emulator, each pass of 6
/// flings down and 6 up added 60–140 live views in Large and 140–400 in Compact at 5 spares (it
/// varies with fling timing, and one Large run barely churned). At 24, Large, Standard and Compact
/// each held a flat view count over every pass, including after a live view-mode switch, and memory
/// was the same as at 5. 12 was nearly enough for Large but not Compact, whose short rows move
/// further per fling. On View All's Airing list (Debug), memory grew about 220 MB per pass without
/// this behavior and held flat with it. A Release build held a flat view count too.
/// </para>
/// <para>
/// The cap only reserves room: the shelf holds as many cards as scrolling actually detaches, so Large
/// settled at 403–423 views at 12, 16 and 24 alike.
/// </para>
/// </summary>
public sealed class SpareCardPoolBehavior : Behavior<CollectionView>
{
    /// <summary>Spare cards kept per view type. See the class summary for how 24 was chosen.</summary>
    public const int SpareCards = 24;

#if ANDROID
    // MAUI's internal ItemViewType.TemplatedItem: the view type of every item built from a plain
    // DataTemplate (Microsoft.Maui.Controls 11.0.10; group headers are 45). Internal, so it is
    // copied rather than referenced. If a MAUI update renumbers it, this sets the cap on a type
    // nothing uses and the list falls back to the default of 5 — today's behaviour, not a break.
    private const int TemplatedItemViewType = 42;
#endif

    protected override void OnAttachedTo(CollectionView bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.HandlerChanged += OnHandlerChanged;
        Apply(bindable);
    }

    protected override void OnDetachingFrom(CollectionView bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        base.OnDetachingFrom(bindable);
    }

    private static void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is CollectionView collectionView)
        {
            Apply(collectionView);
        }
    }

    // Set once per native list. The cap survives the adapter swaps a view-mode change causes:
    // RecyclerView keeps its pool across setAdapter and only empties it.
    private static void Apply(CollectionView collectionView)
    {
#if ANDROID
        if (collectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
        {
            recyclerView.GetRecycledViewPool().SetMaxRecycledViews(TemplatedItemViewType, SpareCards);
        }
#endif
    }
}
