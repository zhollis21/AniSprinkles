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

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(SortDropdown), default(string));

    /// <summary>Heading shown atop the picker, e.g. "Sort Characters".</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
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
        SelectedLabel.Text = selected?.Display ?? Options?.FirstOrDefault()?.Display ?? Title ?? "Sort";
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
                Title ?? "Sort by", options, anchor.ScreenWidth, anchor.ScreenHeight, anchor.Top, anchor.Right);
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

    // Approx picker height for the flip decision: header + divider + padding + rows.
    private const double RowHeightDip = 44;
    private const double ChromeHeightDip = 46;
    private const double GapDip = 6;

    // Measures the pill's on-screen rect so the picker can float just beneath it (or above, when there's
    // no room below), right edges aligned. Android-only (the app's sole target); elsewhere falls back to a
    // top-right card. The scrim is sized to the screen, so the card margin is in that same coordinate space.
    private (double Top, double Right, double ScreenWidth, double ScreenHeight) ComputeAnchor(int optionCount)
    {
#if ANDROID
        if (Pill.Handler?.PlatformView is Android.Views.View native)
        {
            var location = new int[2];
            native.GetLocationOnScreen(location);
            var metrics = native.Context?.Resources?.DisplayMetrics;
            var density = metrics?.Density ?? 1f;
            if (density <= 0)
            {
                density = 1f;
            }

            var screenWidthDip = (metrics?.WidthPixels ?? native.Width) / density;
            var screenHeightDip = (metrics?.HeightPixels ?? native.Height) / density;
            var pillTopDip = location[1] / density;
            var pillBottomDip = (location[1] + native.Height) / density;
            var rightInsetDip = screenWidthDip - ((location[0] + native.Width) / density);

            var estHeight = ChromeHeightDip + (optionCount * RowHeightDip);
            var openUp = (estHeight + GapDip) > (screenHeightDip - pillBottomDip) && pillTopDip > estHeight;
            var top = openUp
                ? Math.Max(pillTopDip - estHeight - GapDip, GapDip)
                : pillBottomDip + GapDip;

            return (top, Math.Max(rightInsetDip, 0), screenWidthDip, screenHeightDip);
        }
#endif
        return (0, 12, 360, 640);
    }
}
