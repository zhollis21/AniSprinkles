using System.Windows.Input;
using AniSprinkles.Utilities;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AniSprinkles.Views;

public partial class ErrorStateView : ContentView
{
    // Both reset the report state, not just ErrorDetails. A NotFound that arrives without an
    // exception leaves ErrorDetails empty and unchanged, so keying the reset on that alone would
    // leave the button reading "Report sent" for the next, different failure.
    public static readonly BindableProperty ErrorTitleProperty =
        BindableProperty.Create(nameof(ErrorTitle), typeof(string), typeof(ErrorStateView), "Something Went Wrong",
            propertyChanged: (b, _, _) => ((ErrorStateView)b).ResetReportState());

    public static readonly BindableProperty ErrorSubtitleProperty =
        BindableProperty.Create(nameof(ErrorSubtitle), typeof(string), typeof(ErrorStateView), "An unexpected error occurred. Try again or check back later.",
            propertyChanged: (b, _, _) => ((ErrorStateView)b).ResetReportState());

    public static readonly BindableProperty ErrorIconGlyphProperty =
        BindableProperty.Create(nameof(ErrorIconGlyph), typeof(string), typeof(ErrorStateView), FluentIconsRegular.ErrorCircle24);

    public static readonly BindableProperty ErrorAccentColorProperty =
        BindableProperty.Create(nameof(ErrorAccentColor), typeof(Color), typeof(ErrorStateView), Colors.OrangeRed);

    public static readonly BindableProperty RetryCommandProperty =
        BindableProperty.Create(nameof(RetryCommand), typeof(ICommand), typeof(ErrorStateView));

    public static readonly BindableProperty ShowRetryButtonProperty =
        BindableProperty.Create(nameof(ShowRetryButton), typeof(bool), typeof(ErrorStateView), true);

    public static readonly BindableProperty ErrorDetailsProperty =
        BindableProperty.Create(nameof(ErrorDetails), typeof(string), typeof(ErrorStateView), string.Empty,
            propertyChanged: (b, _, _) =>
            {
                var view = (ErrorStateView)b;
                view.IsDetailsExpanded = false;
                view.OnPropertyChanged(nameof(HasErrorDetails));
                view.ResetReportState();
            });

    public static readonly BindableProperty IsDetailsExpandedProperty =
        BindableProperty.Create(nameof(IsDetailsExpanded), typeof(bool), typeof(ErrorStateView), false,
            propertyChanged: (b, _, _) => ((ErrorStateView)b).OnPropertyChanged(nameof(DetailsToggleText)));

    public string ErrorTitle
    {
        get => (string)GetValue(ErrorTitleProperty);
        set => SetValue(ErrorTitleProperty, value);
    }

    public string ErrorSubtitle
    {
        get => (string)GetValue(ErrorSubtitleProperty);
        set => SetValue(ErrorSubtitleProperty, value);
    }

    public string ErrorIconGlyph
    {
        get => (string)GetValue(ErrorIconGlyphProperty);
        set => SetValue(ErrorIconGlyphProperty, value);
    }

    public Color ErrorAccentColor
    {
        get => (Color)GetValue(ErrorAccentColorProperty);
        set => SetValue(ErrorAccentColorProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public string ErrorDetails
    {
        get => (string)GetValue(ErrorDetailsProperty);
        set => SetValue(ErrorDetailsProperty, value);
    }

    public bool IsDetailsExpanded
    {
        get => (bool)GetValue(IsDetailsExpandedProperty);
        set => SetValue(IsDetailsExpandedProperty, value);
    }

    public bool ShowRetryButton
    {
        get => (bool)GetValue(ShowRetryButtonProperty);
        set => SetValue(ShowRetryButtonProperty, value);
    }

    public bool HasRetryCommand => RetryCommand is not null && ShowRetryButton;

    public bool HasErrorDetails => !string.IsNullOrWhiteSpace(ErrorDetails);

    public string DetailsToggleText => IsDetailsExpanded ? "Hide technical details" : "Show technical details";

    public ErrorStateView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName is nameof(RetryCommand) or nameof(ShowRetryButton))
        {
            OnPropertyChanged(nameof(HasRetryCommand));
        }
    }

    private void OnToggleDetailsTapped(object? sender, EventArgs e)
    {
        IsDetailsExpanded = !IsDetailsExpanded;
    }

    private async void OnCopyTapped(object? sender, EventArgs e)
    {
        if (HasErrorDetails)
        {
            await AnimatePressAsync(CopyButton);
            await Clipboard.Default.SetTextAsync(ErrorDetails);
            try
            {
                await Toast.Make("Copied to clipboard", ToastDuration.Short).Show();
            }
            catch
            {
                // Toast failures are non-fatal — clipboard write already succeeded.
            }
        }
    }

    private async void OnShareTapped(object? sender, EventArgs e)
    {
        if (HasErrorDetails)
        {
            await AnimatePressAsync(ShareButton);
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Text = ErrorDetails,
                Title = "AniSprinkles Error Details"
            });
        }
    }

    /// <summary>
    /// One report per error shown (#112). The coordinator has its own re-entrancy guard, but that
    /// only stops a double tap while a send is running — without this, three taps a minute apart
    /// would file three events for one fault. Reset whenever the error changes, so the next failure
    /// starts reportable again.
    /// </summary>
    private bool _reportSent;

    /// <summary>Guards the window between the tap and the send completing, during which the button is
    /// still on screen and pressable.</summary>
    private bool _isReporting;

    private async void OnReportTapped(object? sender, EventArgs e)
    {
        if (_reportSent || _isReporting)
        {
            return;
        }

        _isReporting = true;
        try
        {
            await AnimatePressAsync(ReportButton);

            var coordinator = ServiceProviderHelper.GetServiceProvider()
                .GetRequiredService<DiagnosticsReportCoordinator>();

            if (await coordinator.ReportAsync())
            {
                // Only on a confirmed send. Marking it sent after a cancel would leave the user
                // unable to change their mind, and after a failure would strand the report entirely.
                _reportSent = true;
                ReportLabel.Text = "Report sent";
                ReportIconSource.Glyph = FluentIconsRegular.CheckmarkCircle24;
            }
        }
        catch (Exception)
        {
            // The coordinator tells the user about its own failures and does not throw; reaching here
            // means DI itself was unavailable. Throwing out of a gesture handler would take the app
            // down on top of whatever error this view is already displaying.
        }
        finally
        {
            _isReporting = false;
        }
    }

    private void ResetReportState()
    {
        _reportSent = false;

        // Null-guarded because the bindable properties can change before InitializeComponent has run
        // — a page model assigning an error during construction would otherwise NRE here.
        if (ReportLabel is not null)
        {
            ReportLabel.Text = "Report a problem";
        }

        if (ReportIconSource is not null)
        {
            ReportIconSource.Glyph = FluentIconsRegular.Bug24;
        }
    }

    private static async Task AnimatePressAsync(VisualElement element)
    {
        await element.ScaleToAsync(0.92, 60, Easing.CubicOut);
        await element.ScaleToAsync(1.0, 80, Easing.CubicOut);
    }
}
