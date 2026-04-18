namespace AniSprinkles.Utilities;

public static class HandlerHelper
{
    // Recursively disconnects handlers for a view and its visual children. MAUI does not
    // cascade DisconnectHandler() to children, so parent-only disconnection still retains
    // native peers (RecyclerView, image requests) until GC. Stale retention was triggering
    // Runtime.gc() storms that manifested as recurring FocusEvent ANRs.
    public static void DisconnectAll(IView? view)
    {
        if (view is null)
        {
            return;
        }

        if (view is IVisualTreeElement vte)
        {
            foreach (var child in vte.GetVisualChildren())
            {
                if (child is IView childView)
                {
                    DisconnectAll(childView);
                }
            }
        }

        view.Handler?.DisconnectHandler();
    }
}
