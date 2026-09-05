using AniSprinkles.PageModels;
using AniSprinkles.Utilities;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
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
            // The coordinator reports its own failures itself and does not throw, so reaching here
            // means DI was unavailable — which should not happen from a rendered Settings page. Say
            // so anyway: a feature whose entire promise is "you will know what happened" must not
            // have a tap that does nothing. Toast rather than IUserFeedback deliberately — the seam
            // comes from the container that just failed to resolve.
            await ShowUnavailableToastAsync();
        }
        finally
        {
            ReportButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Best-effort, and swallowing on purpose: this is the failure handler, so a toast that itself
    /// fails has nowhere left to report to.
    /// </summary>
    private static async Task ShowUnavailableToastAsync()
    {
        try
        {
            await Toast.Make(DiagnosticsReportCoordinator.UnavailableMessage, ToastDuration.Short).Show();
        }
        catch
        {
            // Nothing above this to tell.
        }
    }
}
