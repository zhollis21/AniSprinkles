using System.Runtime.CompilerServices;
using AniSprinkles.Utilities;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Views;

public partial class MediaDetailsLoadedContentView : ContentView
{
    private readonly ILogger<MediaDetailsLoadedContentView>? _logger;
    private readonly int _viewId;

    public MediaDetailsLoadedContentView()
    {
        InitializeComponent();

        _viewId = RuntimeHelpers.GetHashCode(this);
        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<MediaDetailsLoadedContentView>();
        }
        catch (InvalidOperationException)
        {
        }

        _logger?.LogInformation("LOADEDVIEW MediaDetails[#{ViewId:X}] constructed", _viewId);
    }

    // ── Status dropdown (#194) ──
    // Same spacing as SortDropdown's picker: the card's gap from the button, its clearance from the
    // page edge, and how far above the gesture bar it must end before it flips up.
    private const double StatusGapDip = 6;
    private const double StatusMinEdgeDip = 10;
    private const double StatusBottomSafeDip = 40;

    // A second tap while the popup is open would stack another one over it.
    private bool _isStatusDropdownOpen;

    // Builds the rows from the page model, places the popup against the button, and hands the tapped
    // row to ApplyStatusPickCommand, which owns what each row does (and is unit-tested).
    private async void OnStatusButtonTapped(object? sender, TappedEventArgs e)
    {
        if (_isStatusDropdownOpen || BindingContext is not MediaDetailsPageModel model || model.Media is null)
        {
            return;
        }

        _isStatusDropdownOpen = true;
        try
        {
            StatusDropdownOption[] options =
            [
                // Keys are enum names (nameof, so a typo can't compile): ApplyStatusPick ignores any other key.
                new(nameof(MediaListStatus.Current), model.StatusLabelCurrent, FluentIconsRegular.Eye24),
                new(nameof(MediaListStatus.Planning), model.StatusLabelPlanning, FluentIconsRegular.Bookmark24),
                new(nameof(MediaListStatus.Completed), "Completed", FluentIconsRegular.CheckmarkCircle24),
                new(nameof(MediaListStatus.Paused), "Paused", FluentIconsRegular.PauseCircle24),
                new(nameof(MediaListStatus.Dropped), "Dropped", FluentIconsRegular.DismissCircle24),
                new(nameof(MediaListStatus.Repeating), model.StatusLabelRepeating, FluentIconsRegular.ArrowRepeatAll24),
            ];
            var allowRemove = model.HasListEntry;
            var rowCount = options.Length + (allowRemove ? 1 : 0);
            var anchor = ComputeStatusAnchor(StatusDropdownPopup.EstimateHeight(rowCount));

            var key = await StatusDropdownPopup.ShowAsync(
                options, model.CurrentStatusKey, allowRemove,
                anchor.OpenUp, anchor.Left, anchor.Width, anchor.VEdge, StatusGapDip);

            await model.ApplyStatusPickCommand.ExecuteAsync(key);
        }
        finally
        {
            _isStatusDropdownOpen = false;
        }
    }

    // Page-space bounds of the status button, and whether the card must open above it. Mirrors
    // SortDropdown.ComputeAnchor, anchored to the button itself rather than a header bar.
    private (bool OpenUp, double Left, double Width, double VEdge) ComputeStatusAnchor(double estHeightDip)
    {
#if ANDROID
        if (StatusButton.Handler?.PlatformView is Android.Views.View native)
        {
            var metrics = native.Context?.Resources?.DisplayMetrics;
            var density = metrics?.Density ?? 1f;
            if (density <= 0)
            {
                density = 1f;
            }

            var loc = new int[2];
            native.GetLocationOnScreen(loc);

            var bars = native.RootWindowInsets?.GetInsets(Android.Views.WindowInsets.Type.SystemBars());
            double originX = bars?.Left ?? 0;
            double originY = bars?.Top ?? 0;
            var screenHeightPx = metrics?.HeightPixels ?? native.Height;
            var pageHeightDip = (screenHeightPx - originY - (bars?.Bottom ?? 0)) / density;

            var left = (loc[0] - originX) / density;
            var width = native.Width / density;
            var top = (loc[1] - originY) / density;
            var bottom = (loc[1] + native.Height - originY) / density;

            // The room the card may use is the page's own view — between the navigation bar and the tab bar —
            // not the whole screen: a card that ends over the tab bar reads as off-screen too. Falls back to
            // the screen less the gesture-bar margin if the page has no native view yet.
            var limitTop = StatusMinEdgeDip;
            var limitBottom = pageHeightDip - StatusBottomSafeDip;
            if (Shell.Current?.CurrentPage?.Handler?.PlatformView is Android.Views.View pageView && pageView.Height > 0)
            {
                var pageLoc = new int[2];
                pageView.GetLocationOnScreen(pageLoc);
                limitTop = ((pageLoc[1] - originY) / density) + StatusMinEdgeDip;
                limitBottom = Math.Min(limitBottom, ((pageLoc[1] + pageView.Height - originY) / density) - StatusMinEdgeDip);
            }

            var openUp = (bottom + StatusGapDip + estHeightDip) > limitBottom
                && (top - StatusGapDip - estHeightDip) >= limitTop;

            return (openUp, left, width, openUp ? top : bottom);
        }
#endif
        return (false, 12, StatusButton.Width > 0 ? StatusButton.Width : 300, 0);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        _logger?.LogInformation(
            "LOADEDVIEW MediaDetails[#{ViewId:X}] OnHandlerChanged (handler={HasHandler})",
            _viewId, Handler is not null);
    }
}
