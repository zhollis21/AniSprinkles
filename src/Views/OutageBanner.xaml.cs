namespace AniSprinkles.Views;

/// <summary>
/// Page-level persistent banner that is shown whenever
/// <see cref="IOutageStateService.IsOutage"/> is true. Resolves the service from
/// DI in its constructor so pages can drop <c>&lt;views:OutageBanner /&gt;</c> in
/// without wiring anything through their page model.
/// </summary>
public partial class OutageBanner : ContentView
{
    public OutageBanner()
    {
        InitializeComponent();
        // Resolve the singleton from DI at construction time. Fallback to a no-op
        // instance keeps the XAML designer + tests happy if the Maui handler isn't
        // ready yet.
        BindingContext =
            Application.Current?.Handler?.MauiContext?.Services?.GetService<IOutageStateService>()
            ?? new OutageStateService();
    }
}
