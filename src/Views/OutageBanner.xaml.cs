using AniSprinkles.Utilities;

namespace AniSprinkles.Views;

/// <summary>
/// Page-level persistent banner that is shown whenever
/// <see cref="IOutageStateService.IsOutage"/> is true. Resolves the service from
/// DI so pages can drop <c>&lt;views:OutageBanner /&gt;</c> in without wiring
/// anything through their page model.
/// </summary>
public partial class OutageBanner : ContentView
{
    public OutageBanner()
    {
        InitializeComponent();
        TryBindOutageStateService();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        // Re-resolve once the handler is wired in case the constructor ran before
        // DI was ready. Without this, the banner would stay bound to a fallback
        // instance and never receive real outage updates.
        TryBindOutageStateService();
    }

    private void TryBindOutageStateService()
    {
        try
        {
            var service = ServiceProviderHelper.GetServiceProvider().GetService<IOutageStateService>();
            if (service is not null && !ReferenceEquals(BindingContext, service))
            {
                BindingContext = service;
            }
        }
        catch (InvalidOperationException)
        {
            // DI not yet ready (e.g. running in the XAML designer); OnHandlerChanged
            // will try again once the view is attached.
        }
    }
}
