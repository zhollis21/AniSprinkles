using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;

namespace AniSprinkles.Views;

/// <summary>
/// Bottom-sheet menu shown on long-press of a My Anime entry. Renders one row per supplied
/// <see cref="MyAnimeEntryAction"/> (the caller pre-filters by entry status) and closes with the
/// chosen action. Mirrors <see cref="MoveToListPopup"/>'s sheet styling and Android edge-to-edge
/// handling.
/// </summary>
public partial class ActionMenuPopup : Popup<object>
{
    private static readonly Dictionary<MyAnimeEntryAction, (string Label, string Glyph, string ColorKey, bool IsDestructive)> ActionMeta = new()
    {
        [MyAnimeEntryAction.OpenDetails]   = ("Open details",     FluentIconsRegular.Info24,            "RainbowBlue",   false),
        [MyAnimeEntryAction.EditProgress]  = ("Edit progress",    FluentIconsRegular.Edit24,            "RainbowCyan",   false),
        [MyAnimeEntryAction.MarkCompleted] = ("Mark as completed", FluentIconsRegular.CheckmarkCircle24, "RainbowGreen",  false),
        [MyAnimeEntryAction.Rate]          = ("Rate",             FluentIconsRegular.Star24,            "RainbowYellow", false),
        [MyAnimeEntryAction.MoveToList]    = ("Move to list",     FluentIconsRegular.FolderArrowRight24, "RainbowPurple", false),
        [MyAnimeEntryAction.Remove]        = ("Remove from list", FluentIconsRegular.Delete24,          "RainbowRed",    true),
    };

    public ActionMenuPopup(string animeTitle, IReadOnlyList<MyAnimeEntryAction> actions)
    {
        InitializeComponent();
        TitleLabel.Text = animeTitle;
        BuildActionRows(actions);

        // Anchor as a bottom sheet. CommunityToolkit Popup V2 re-declares these as `new` BindableProperties,
        // so they must be set in code (not on the XAML root) to reach the binding the PopupBorder reads.
        // (Same lesson as MoveToListPopup/SortPopup; the rounded-top sheet itself is the toolkit's
        // PopupBorder, shaped via PopupOptions.)
        Padding = new Thickness(0);
        VerticalOptions = LayoutOptions.End;
        HorizontalOptions = LayoutOptions.Center;
        // Must NOT be all-zero: the toolkit's MarginConverter treats Thickness(0) as "empty" and swaps in
        // the default Thickness(30). The 1dp top is invisible for a bottom-anchored sheet.
        Margin = new Thickness(0, 1, 0, 0);

        var display = DeviceDisplay.MainDisplayInfo;
        SheetBorder.WidthRequest = display.Width / display.Density;

        // Reach the physical bottom edge behind the gesture bar; finish once attached (see MoveToListPopup).
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

        // The sheet colour now fills the navigation-bar strip; pad the content up by the inset so the last
        // row still clears the gesture bar.
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

    private void BuildActionRows(IReadOnlyList<MyAnimeEntryAction> actions)
    {
        foreach (var action in actions)
        {
            if (!ActionMeta.TryGetValue(action, out var meta))
            {
                continue;
            }

            var accentColor = GetResourceColor(meta.ColorKey);

            var icon = new Image
            {
                WidthRequest = 20,
                HeightRequest = 20,
                VerticalOptions = LayoutOptions.Center,
                Source = new FontImageSource
                {
                    Glyph = meta.Glyph,
                    FontFamily = FluentIconsRegular.FontFamily,
                    Color = accentColor,
                    Size = 20
                }
            };

            var textLabel = new Label
            {
                Text = meta.Label,
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

            var capturedAction = action;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => OnActionTapped(capturedAction);
            border.GestureRecognizers.Add(tap);

            ActionsLayout.Children.Add(border);
        }
    }

    private async void OnActionTapped(MyAnimeEntryAction action)
    {
        await CloseAsync(action);
    }

    private static Color GetResourceColor(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c)
        {
            return c;
        }

        return Colors.White;
    }
}
