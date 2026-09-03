using AniSprinkles.PageModels;
using AniSprinkles.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace AniSprinkles.Views;

/// <summary>
/// The Settings entry point for sending a diagnostic report (#112).
/// <para>
/// Resolves the coordinator from DI rather than binding to a page model, because it has to render in
/// the signed-out Settings state too — where there is no loaded page model to bind to. That state is
/// not an edge case here: a sign-in failure is one of the things most worth reporting.
/// </para>
/// </summary>
public partial class DiagnosticsReportView : ContentView
{
    public DiagnosticsReportView()
    {
        InitializeComponent();
    }

    private async void OnReportClicked(object? sender, EventArgs e)
    {
        // Disabled for the duration rather than relying on the coordinator's own guard alone. The
        // send waits on a network flush, so the button would otherwise sit live and pressable for
        // seconds while apparently doing nothing.
        ReportButton.IsEnabled = false;
        try
        {
            var coordinator = ServiceProviderHelper.GetServiceProvider()
                .GetRequiredService<DiagnosticsReportCoordinator>();

            await coordinator.ReportAsync();
        }
        catch (Exception)
        {
            // The coordinator reports its own failures to the user and does not throw; reaching here
            // means DI itself was unavailable. Nothing useful to say, and throwing out of a Clicked
            // handler would take the app down.
        }
        finally
        {
            ReportButton.IsEnabled = true;
        }
    }
}
