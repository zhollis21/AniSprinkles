using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using IconFont.Maui.FluentIcons;
using Microsoft.Maui.Controls.Shapes;

namespace AniSprinkles.Views;

/// <summary>
/// A compact sort picker (#85) that floats just beneath (or above) the tapped <see cref="SortDropdown"/>,
/// AniList-web style.
/// <para>
/// CommunityToolkit.Maui 14.x hosts a Popup as a full-screen modal page whose <c>PopupPageLayout</c> places
/// a single <c>PopupBorder</c> (the visible card) inside a full-screen <c>Grid</c>, aligned by the Popup's
/// own <c>VerticalOptions</c>/<c>HorizontalOptions</c> (where
/// <c>Fill</c> is coerced to <c>Center</c>) and offset by <c>Margin</c> in page
/// coordinates. We therefore: set the Popup to <c>Start</c>/<c>Start</c> and position it with
/// <c>Margin</c>; give the card its rounded shape + shadow through
/// <see cref="PopupOptions.Shape"/>/<see cref="PopupOptions.Shadow"/> so the positioned <c>PopupBorder</c>
/// IS the card (no nested Border whose shadow would offset us); and let the toolkit supply the dim
/// (<see cref="PopupOptions.PageOverlayColor"/>) and tap-outside dismissal. The anchor coordinates are
/// measured in page space by <see cref="SortDropdown.ComputeAnchor"/>.
/// </para>
/// Returns the chosen <see cref="SortOption.Code"/>, or <c>null</c> when dismissed; it never mutates
/// selection itself, so a failed server sort can revert the highlight upstream.
/// </summary>
public sealed class SortPopup : Popup<string?>
{
    public const double CardWidth = 210;

    // Smallest top margin we'll allow so a flipped-up card never runs under the status bar.
    private const double MinTopDip = 8;

    // The toolkit's PopupBorder insets our content by the popup's default Padding (15dp) on every side, and
    // that Padding can't be reliably zeroed from a Popup subclass. Popup.Margin positions the *outer* border,
    // so we subtract this inset from the margin to land the *visible content* on the measured anchor.
    public const double ContentInsetDip = 15;

    public static async Task<string?> ShowAsync(
        IReadOnlyList<SortOption> options,
        bool openUp,
        double cardLeftDip,
        double pillVEdgeDip,
        double gapDip,
        CancellationToken cancellationToken = default)
    {
        if (Shell.Current?.CurrentPage is not { } page)
        {
            return null;
        }

        var popup = new SortPopup(options, openUp, cardLeftDip, pillVEdgeDip, gapDip);
        var popupOptions = new PopupOptions
        {
            // The toolkit's PopupBorder is the visible card: give it our rounded shape + stroke so
            // Popup.Margin positions the card itself (no nested Border whose shadow bleed would offset us).
            Shape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(14),
                Stroke = Res<Color>("Gray600"),
                StrokeThickness = 1,
            },
            // No Shadow: a Shadow expands the PopupBorder's layout box by its bleed (~40dp) and centers the
            // visible card inside it, which silently offset our anchored position. The 1px stroke + the dim
            // page overlay give enough separation. (Re-add only if we also offset Margin by the bleed.)
            Shadow = null,
            // Toolkit-owned scrim dim + tap-outside dismissal (default CanBeDismissedByTappingOutsideOfPopup).
            PageOverlayColor = Color.FromArgb("#59000000"),
        };

        try
        {
            var result = await page.ShowPopupAsync<string?>(popup, popupOptions, cancellationToken);
            return result.WasDismissedByTappingOutsideOfPopup ? null : result.Result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private SortPopup(
        IReadOnlyList<SortOption> options,
        bool openUp,
        double cardLeftDip,
        double pillVEdgeDip,
        double gapDip)
    {
        // The PopupBorder paints the card fill; our content is just the rows.
        BackgroundColor = Res<Color>("SecondaryBackground");
        // Zero our own padding: it works and removing it adds a second 15dp inset between the positioned
        // border and the visible content (we compensate for the toolkit's single inherent inset below).
        Padding = 0;
        // Start/Start: the toolkit coerces Fill->Center, but Start passes through, making the page content's
        // top-left the origin so our Margin positions the card absolutely (in page coordinates).
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;

        // No heading: the card opens directly above/below the pill the user just tapped, so the section is
        // already obvious. Keep the top/bottom padding so the first/last row clears the rounded corners.
        var content = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(0, 6),
            WidthRequest = CardWidth,
        };
        foreach (var option in options)
        {
            content.Add(BuildRow(option));
        }

        Content = content;

        // cardLeftDip is the desired *content* left; the margin (positioning the outer border) is inset less.
        var marginLeft = cardLeftDip - ContentInsetDip;

        if (openUp)
        {
            // The exact top needs the card's real height, unknown until it lays out. Seed with an estimate so
            // the first frame is already close, then refine on SizeChanged so the gap is exact. pillVEdgeDip
            // is the pill's TOP here; the card's visible bottom sits gapDip above it.
            var estHeight = 12 + (options.Count * 44);
            Margin = new Thickness(marginLeft, UpMarginTop(pillVEdgeDip, estHeight, gapDip), 0, 0);
            EventHandler? onSized = null;
            onSized = (_, _) =>
            {
                if (content.Height <= 0)
                {
                    return;
                }

                Margin = new Thickness(marginLeft, UpMarginTop(pillVEdgeDip, content.Height, gapDip), 0, 0);
                content.SizeChanged -= onSized;
            };
            content.SizeChanged += onSized;
        }
        else
        {
            // pillVEdgeDip is the pill's BOTTOM; the visible content top sits gapDip below it (less the inset).
            // Clamp to MinTopDip so a degenerate anchor (e.g. the handler-not-ready fallback's VEdge of 0)
            // can't produce a negative top margin that pushes the card under the status bar — mirrors the
            // up-open path's clamp.
            var topDip = Math.Max(pillVEdgeDip + gapDip - ContentInsetDip, MinTopDip);
            Margin = new Thickness(marginLeft, topDip, 0, 0);
        }
    }

    // Top margin so the card's visible bottom lands gapDip above the pill top: content bottom =
    // marginTop + inset + height, so marginTop = pillTop - height - gap - inset.
    private static double UpMarginTop(double pillTopDip, double cardHeightDip, double gapDip)
    {
        return Math.Max(pillTopDip - cardHeightDip - gapDip - ContentInsetDip, MinTopDip);
    }

    private View BuildRow(SortOption option)
    {
        var label = new Label
        {
            Text = option.Display,
            Style = Res<Style>("Body2"),
            TextColor = option.IsSelected ? Res<Color>("RainbowCyan") : Res<Color>("Gray100"),
            VerticalTextAlignment = TextAlignment.Center,
        };
        Grid.SetColumn(label, 0);

        var check = new Image
        {
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = option.IsSelected,
            Source = new FontImageSource
            {
                Glyph = FluentIconsRegular.Checkmark24,
                FontFamily = FluentIconsRegular.FontFamily,
                Color = Res<Color>("RainbowCyan"),
                Size = 16,
            },
        };
        Grid.SetColumn(check, 1);

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 10,
        };
        grid.Add(label);
        grid.Add(check);

        var border = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(16, 11),
            // Transparent fill so the card shows through — must be a Brush (the implicit Border style sets
            // Background and a Brush wins over a Color).
            Background = Brush.Transparent,
            Content = grid,
        };

        var code = option.Code;
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await CloseAsync(code);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private static T Res<T>(string key) => (T)Application.Current!.Resources[key];
}
