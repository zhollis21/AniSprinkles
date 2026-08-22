using AniSprinkles.Converters;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;

namespace AniSprinkles.Views;

public partial class MoveToListPopup : Popup<object>
{
    private static readonly RainbowAccentConverter RainbowConverter = new();

    private static readonly (MediaListStatus Status, string Label, string Glyph)[] AllStatuses =
    [
        (MediaListStatus.Current,   "Watching",    FluentIconsRegular.Eye24),
        (MediaListStatus.Planning,  "Planning",    FluentIconsRegular.Bookmark24),
        (MediaListStatus.Completed, "Completed",   FluentIconsRegular.CheckmarkCircle24),
        (MediaListStatus.Paused,    "Paused",      FluentIconsRegular.PauseCircle24),
        (MediaListStatus.Dropped,   "Dropped",     FluentIconsRegular.DismissCircle24),
        (MediaListStatus.Repeating, "Rewatching",  FluentIconsRegular.ArrowRepeatAll24),
    ];

    /// <summary>
    /// <paramref name="currentStatus"/> null shows every status (add-to-list: nothing to omit);
    /// <paramref name="allowRemove"/> false hides the Remove row (the media isn't on the list yet).
    /// </summary>
    public MoveToListPopup(string animeTitle, MediaListStatus? currentStatus, bool allowRemove, string? subtitle)
    {
        InitializeComponent();
        TitleLabel.Text = animeTitle;
        if (subtitle is not null)
        {
            SubtitleLabel.Text = subtitle;
        }

        if (!allowRemove)
        {
            DeleteDivider.IsVisible = false;
            DeleteRow.IsVisible = false;
        }

        BuildStatusRows(currentStatus);

        // Anchor as a bottom sheet. CommunityToolkit Popup V2 re-declares these as `new` BindableProperties,
        // so they must be set in code (not on the XAML root) to reach the binding the PopupBorder reads.
        // Zero the toolkit's default 15dp content inset so the sheet content uses the full width. (Same lesson
        // as SortPopup; the rounded-top sheet itself is the toolkit's PopupBorder, shaped via PopupOptions.)
        Padding = new Thickness(0);
        VerticalOptions = LayoutOptions.End;
        // Fill is coerced to Center by the toolkit; full width comes from the screen-width WidthRequest below.
        HorizontalOptions = LayoutOptions.Center;
        // Must NOT be all-zero: the toolkit's MarginConverter treats Thickness(0) as "empty" and swaps in the
        // default Thickness(30). The 1dp top is invisible for a bottom-anchored sheet; sides/bottom stay flush.
        Margin = new Thickness(0, 1, 0, 0);

        var display = DeviceDisplay.MainDisplayInfo;
        SheetBorder.WidthRequest = display.Width / display.Density;

        // The sheet should reach the physical bottom edge, behind the gesture bar. The toolkit hosts the popup
        // in a Grid (PopupPageLayout) which, since .NET 10, defaults to SafeAreaEdges=Container and so reserves
        // the Android navigation-bar inset — leaving the End-anchored sheet stopping above the gesture bar with
        // the dim scrim showing through. We can only reach that Grid once attached, so finish in OnOpened.
        Opened += OnOpenedGoEdgeToEdge;
    }

    private void OnOpenedGoEdgeToEdge(object? sender, EventArgs e)
    {
        Opened -= OnOpenedGoEdgeToEdge;

        // Make the popup chrome (the toolkit's Border + Grid ancestors, up to the page) edge-to-edge so the
        // bottom-anchored sheet extends behind the navigation bar instead of stopping above it.
        for (Element? element = this; element is not null and not Page; element = element.Parent)
        {
            switch (element)
            {
                case Layout layout:
                    layout.SafeAreaEdges = SafeAreaEdges.None;
                    break;
                case ContentView contentView:
                    contentView.SafeAreaEdges = SafeAreaEdges.None;
                    break;
                case Border border:
                    border.SafeAreaEdges = SafeAreaEdges.None;
                    break;
            }
        }

        // The sheet colour now fills the navigation-bar strip; pad the content up by the inset so the last row
        // still clears the gesture bar.
        var bottomInsetDip = GetBottomSystemBarInsetDip();
        if (bottomInsetDip > 0)
        {
            SheetContent.Padding = new Thickness(0, 0, 0, bottomInsetDip);
        }
    }

    private static double GetBottomSystemBarInsetDip()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        var insets = activity?.Window?.DecorView?.RootWindowInsets?
            .GetInsets(Android.Views.WindowInsets.Type.SystemBars());
        var density = activity?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (insets is not null && density > 0)
        {
            return insets.Bottom / density;
        }
#endif
        return 0;
    }

    private void BuildStatusRows(MediaListStatus? currentStatus)
    {
        foreach (var (status, label, glyph) in AllStatuses)
        {
            if (status == currentStatus)
            {
                continue;
            }

            var accentColor = GetAccentColor(label);

            var icon = new Image
            {
                WidthRequest = 20,
                HeightRequest = 20,
                VerticalOptions = LayoutOptions.Center,
                Source = new FontImageSource
                {
                    Glyph = glyph,
                    FontFamily = FluentIconsRegular.FontFamily,
                    Color = accentColor,
                    Size = 20
                }
            };

            var textLabel = new Label
            {
                Text = label,
                Style = (Style)Application.Current!.Resources["Body2"],
                TextColor = accentColor,
                VerticalTextAlignment = TextAlignment.Center
            };

            var grid = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
                ColumnSpacing = 12
            };
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(textLabel, 1);
            grid.Children.Add(icon);
            grid.Children.Add(textLabel);

            var border = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 0,
                Padding = new Thickness(14, 12),
                BackgroundColor = Colors.Transparent,
                Content = grid
            };

            var capturedStatus = status;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => OnStatusTapped(capturedStatus);
            border.GestureRecognizers.Add(tap);

            StatusOptionsLayout.Children.Add(border);
        }
    }

    private async void OnStatusTapped(MediaListStatus status)
    {
        await CloseAsync(status);
    }

    private async void OnDeleteTapped(object? sender, EventArgs e)
    {
        await CloseAsync("delete");
    }

    private static Color GetAccentColor(string label)
    {
        var result = RainbowConverter.Convert(label, typeof(Color), null!, System.Globalization.CultureInfo.InvariantCulture);
        return result is Color c ? c : Colors.White;
    }
}
