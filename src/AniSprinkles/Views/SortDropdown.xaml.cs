using System.Windows.Input;

namespace AniSprinkles.Views;

/// <summary>
/// The reusable sort trigger (#85): a chevron pill that shows the active sort's label and, on tap, opens
/// the <see cref="SortPopup"/> picker. It owns no selection state — the tapped code is handed to
/// <see cref="SelectSortCommand"/> (the page model's <c>Select{Section}SortCommand</c>), which drives the
/// actual sort and re-syncs <see cref="SelectedCode"/>, so a failed server sort leaves the highlight intact.
/// </summary>
public partial class SortDropdown : ContentView
{
    public SortDropdown()
    {
        InitializeComponent();
        UpdateLabel();
    }

    public static readonly BindableProperty OptionsProperty =
        BindableProperty.Create(nameof(Options), typeof(IReadOnlyList<SortOption>), typeof(SortDropdown), null, propertyChanged: OnVisualChanged);

    public IReadOnlyList<SortOption>? Options
    {
        get => (IReadOnlyList<SortOption>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public static readonly BindableProperty SelectedCodeProperty =
        BindableProperty.Create(nameof(SelectedCode), typeof(string), typeof(SortDropdown), default(string), propertyChanged: OnVisualChanged);

    /// <summary>The active sort code; the pill label derives from the matching option's Display.</summary>
    public string? SelectedCode
    {
        get => (string?)GetValue(SelectedCodeProperty);
        set => SetValue(SelectedCodeProperty, value);
    }

    public static readonly BindableProperty SelectSortCommandProperty =
        BindableProperty.Create(nameof(SelectSortCommand), typeof(ICommand), typeof(SortDropdown), null);

    public ICommand? SelectSortCommand
    {
        get => (ICommand?)GetValue(SelectSortCommandProperty);
        set => SetValue(SelectSortCommandProperty, value);
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SortDropdown)bindable).UpdateLabel();

    private void UpdateLabel()
    {
        var selected = Options?.FirstOrDefault(o => string.Equals(o.Code, SelectedCode, StringComparison.Ordinal));
        SelectedLabel.Text = selected?.Display ?? Options?.FirstOrDefault()?.Display ?? "Sort";
    }

    private bool _isOpen;

    private async void OnTapped(object? sender, EventArgs e)
    {
        // Re-entrancy guard: a second tap while the picker is up would stack popups.
        if (_isOpen || Options is not { Count: > 0 } options)
        {
            return;
        }

        _isOpen = true;
        try
        {
            var anchor = ComputeAnchor(options.Count);
            var result = await SortPopup.ShowAsync(
                options, anchor.OpenUp, anchor.CardLeft, anchor.VEdge, GapDip);
            if (!string.IsNullOrEmpty(result)
                && !string.Equals(result, SelectedCode, StringComparison.Ordinal)
                && SelectSortCommand?.CanExecute(result) == true)
            {
                SelectSortCommand.Execute(result);
            }
        }
        finally
        {
            _isOpen = false;
        }
    }

    // Approx picker height for the flip decision: top/bottom card padding + rows (the title is gone).
    private const double RowHeightDip = 44;
    private const double ChromeHeightDip = 12;
    private const double GapDip = 6;
    // Keep the card clear of the screen edges so its rounded corners + shadow aren't clipped (right) and it
    // doesn't run under the gesture/home-indicator (bottom). Constant insets are adequate for ≤6 short rows;
    // querying real RootWindowInsets would be more precise but isn't worth it at this size.
    private const double MinEdgeDip = 10;
    private const double BottomSafeDip = 40;

    // Computes where the picker should open: floating just below the section header bar (or above it, when
    // there's no room below), right edge aligned under the chevron. We anchor vertically to the whole header
    // BAR — not just the pill — because the pill is centered in a taller colored header, so a gap below the
    // pill alone still overlaps the bar; clearing the bar is what "opens outside the pill" means.
    // Coordinates are converted into the popup page's space (which starts below the status bar) by
    // subtracting the system-bar insets. Android-only; if the pill's platform handler isn't ready yet,
    // falls back to a fixed card near the top-left.
    private (bool OpenUp, double CardLeft, double VEdge) ComputeAnchor(int optionCount)
    {
#if ANDROID
        if (Pill.Handler?.PlatformView is Android.Views.View native)
        {
            var metrics = native.Context?.Resources?.DisplayMetrics;
            var density = metrics?.Density ?? 1f;
            if (density <= 0)
            {
                density = 1f;
            }

            var pillLoc = new int[2];
            native.GetLocationOnScreen(pillLoc);

            // Page-space origin = the system-bar insets (status bar on top). The toolkit's popup page lays out
            // its content below the status bar, so Popup.Margin is measured from there; converting the pill's
            // absolute on-screen coords into that space means subtracting the status-bar inset. We read the
            // insets off the pill's RootWindowInsets (reliable; the Activity/content-view lookups returned null).
            var bars = native.RootWindowInsets?.GetInsets(Android.Views.WindowInsets.Type.SystemBars());
            double originX = bars?.Left ?? 0;
            double originY = bars?.Top ?? 0;
            var screenWidthPx = metrics?.WidthPixels ?? native.Width;
            var screenHeightPx = metrics?.HeightPixels ?? native.Height;
            var pageWidthDip = (screenWidthPx - originX - (bars?.Right ?? 0)) / density;
            var pageHeightDip = (screenHeightPx - originY - (bars?.Bottom ?? 0)) / density;

            var pillRightDip = ((pillLoc[0] + native.Width) - originX) / density;

            // Find the section header bar: walk up to the nearest wide-but-short ancestor (the full-width
            // colored header row that the pill sits at the right of). Clearing this — rather than just the
            // pill — keeps the card from opening over the header. Falls back to the pill if none qualifies.
            var bar = native;
            var ancestor = native.Parent;
            for (var i = 0; i < 6 && ancestor is Android.Views.View av; i++)
            {
                if (av.Width >= screenWidthPx * 0.6 && av.Height <= screenHeightPx * 0.25)
                {
                    bar = av;
                }

                ancestor = av.Parent;
            }

            var barLoc = new int[2];
            bar.GetLocationOnScreen(barLoc);
            var barTopDip = (barLoc[1] - originY) / density;
            var barBottomDip = ((barLoc[1] + bar.Height) - originY) / density;

            var estHeight = ChromeHeightDip + (optionCount * RowHeightDip);
            var bottomLimitDip = pageHeightDip - BottomSafeDip;
            // Flip up only when there isn't room below the bar within the safe area AND there is room above.
            // The estimate only drives this decision; the popup positions itself off the card's real height.
            var openUp = (barBottomDip + GapDip + estHeight) > bottomLimitDip
                && barTopDip > (estHeight + GapDip + MinEdgeDip);

            // Right-align the card's right edge under the pill's right edge (the chevron), clamped so the
            // rounded corners stay clear of the screen edges.
            var maxLeft = Math.Max(MinEdgeDip, pageWidthDip - SortPopup.CardWidth - MinEdgeDip);
            var cardLeft = Math.Clamp(pillRightDip - SortPopup.CardWidth, MinEdgeDip, maxLeft);

            // The header-bar edge the card hugs: its TOP when flipping up, its BOTTOM when opening down.
            var vEdge = openUp ? barTopDip : barBottomDip;

            return (openUp, cardLeft, vEdge);
        }
#endif
        return (false, 12, 0);
    }
}
