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
                options, anchor.OpenUp, anchor.CardLeft, anchor.PillVEdge, GapDip);
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

    // Measures the pill's rect in the popup's coordinate space so the picker can float just beneath it (or
    // above, when there's no room below), right edges aligned under the chevron. The toolkit positions the
    // popup's content-sized host Border within the modal page via the Popup's Margin, and that page's origin
    // is the Activity content view (android.R.id.content) — so we convert the pill's absolute on-screen
    // coordinates into that space by subtracting the content view's origin (cancels the status-bar offset).
    // Android-only (the app's sole target); elsewhere falls back to a fixed top-right card.
    private (bool OpenUp, double CardLeft, double PillVEdge) ComputeAnchor(int optionCount)
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
            var pillTopDip = (pillLoc[1] - originY) / density;
            var pillBottomDip = ((pillLoc[1] + native.Height) - originY) / density;

            var estHeight = ChromeHeightDip + (optionCount * RowHeightDip);
            var bottomLimitDip = pageHeightDip - BottomSafeDip;
            // Flip up only when there isn't room below within the safe area AND there is room above. The
            // estimate only drives this decision; the popup positions itself off the card's real height.
            var openUp = (pillBottomDip + GapDip + estHeight) > bottomLimitDip
                && pillTopDip > (estHeight + GapDip + MinEdgeDip);

            // Right-align the card's right edge under the pill's right edge (the chevron), clamped so the
            // rounded corners + shadow stay clear of the screen edges.
            var maxLeft = Math.Max(MinEdgeDip, pageWidthDip - SortPopup.CardWidth - MinEdgeDip);
            var cardLeft = Math.Clamp(pillRightDip - SortPopup.CardWidth, MinEdgeDip, maxLeft);

            // The pill edge the card hugs: its TOP when flipping up, its BOTTOM when opening down.
            var pillVEdge = openUp ? pillTopDip : pillBottomDip;

            return (openUp, cardLeft, pillVEdge);
        }
#endif
        return (false, 12, 0);
    }
}
