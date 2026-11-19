using AniSprinkles.Converters;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using IconFont.Maui.FluentIcons;
using Microsoft.Maui.Controls.Shapes;

namespace AniSprinkles.Views;

/// <summary>One row of <see cref="StatusDropdownPopup"/>: the value handed back on tap, its wording and glyph.</summary>
public sealed record StatusDropdownOption(string Key, string Label, string Glyph);

/// <summary>
/// The details page's status list as an anchored card that floats below the
/// status button, or above it when there isn't room below — the inline dropdown it replaces opened
/// off-screen near the bottom of the page. Same mechanics as <see cref="SortPopup"/> (Start/Start
/// alignment, positioned by <c>Margin</c> in page space, the toolkit's PopupBorder as the card), sized to
/// the button's width. Rows mirror the old dropdown: every status, the current one highlighted, then
/// Remove when there is an entry.
/// <para>
/// Returns the tapped <see cref="StatusDropdownOption.Key"/>, <see cref="RemoveKey"/>, or <c>null</c>
/// when dismissed. It changes nothing itself; the caller runs the page model's commands.
/// </para>
/// </summary>
public sealed class StatusDropdownPopup : Popup<string?>
{
    public const string RemoveKey = MediaDetailsPageModel.RemoveStatusKey;

    // Rough row height (a row plus its divider) and the card's fixed height — the toolkit's 15dp inset top
    // and bottom plus our 4dp padding — used only for the flip decision and the first up-open frame.
    public const double EstRowHeightDip = 44;
    public const double EstChromeHeightDip = (2 * SortPopup.ContentInsetDip) + 8;

    private const double MinTopDip = 8;

    private static readonly RainbowAccentConverter RainbowConverter = new();

    public static async Task<string?> ShowAsync(
        IReadOnlyList<StatusDropdownOption> options,
        string? currentKey,
        bool allowRemove,
        bool openUp,
        double cardLeftDip,
        double cardWidthDip,
        double anchorVEdgeDip,
        double gapDip)
    {
        if (Shell.Current?.CurrentPage is not { } page)
        {
            return null;
        }

        var popup = new StatusDropdownPopup(options, currentKey, allowRemove, openUp, cardLeftDip, cardWidthDip, anchorVEdgeDip, gapDip);
        var popupOptions = new PopupOptions
        {
            // See SortPopup: the PopupBorder is the card, and a Shadow would offset the anchored position.
            Shape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(10),
                Stroke = Res<Color>("Gray600"),
                StrokeThickness = 1,
            },
            Shadow = null,
            PageOverlayColor = Color.FromArgb("#59000000"),
        };

        var result = await page.ShowPopupAsync<string?>(popup, popupOptions);
        return result.WasDismissedByTappingOutsideOfPopup ? null : result.Result;
    }

    /// <summary>Estimated visible card height for <paramref name="rowCount"/> rows, for the flip decision.</summary>
    public static double EstimateHeight(int rowCount) => EstChromeHeightDip + (rowCount * EstRowHeightDip);

    private StatusDropdownPopup(
        IReadOnlyList<StatusDropdownOption> options,
        string? currentKey,
        bool allowRemove,
        bool openUp,
        double cardLeftDip,
        double cardWidthDip,
        double anchorVEdgeDip,
        double gapDip)
    {
        BackgroundColor = Res<Color>("SecondaryBackground");
        Padding = 0;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;

        var content = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(4, 4),
            // The toolkit's PopupBorder (the visible card) insets content by 15dp on each side, so the card
            // matches the button only if the content is that much narrower.
            WidthRequest = Math.Max(cardWidthDip - (2 * SortPopup.ContentInsetDip), 0),
        };

        for (var i = 0; i < options.Count; i++)
        {
            if (i > 0)
            {
                content.Add(Divider());
            }

            var option = options[i];
            var isCurrent = string.Equals(option.Key, currentKey, StringComparison.Ordinal);
            // Same colouring as the old inline rows: each row in
            // its status accent, the current one white on a faint accent wash.
            var accent = RainbowConverter.Convert(option.Key, typeof(Color), null, System.Globalization.CultureInfo.InvariantCulture) as Color
                ?? Colors.White;
            content.Add(BuildRow(
                option.Key,
                option.Label,
                option.Glyph,
                isCurrent ? Colors.White : accent,
                isCurrent ? new SolidColorBrush(accent.WithAlpha(0.05f)) : Brush.Transparent));
        }

        if (allowRemove)
        {
            content.Add(Divider());
            var gray = Res<Color>("Gray400");
            content.Add(BuildRow(RemoveKey, "Remove from List", FluentIconsRegular.Delete24, gray, Brush.Transparent));
        }

        Content = content;

        // Unlike SortPopup, the anchor here is the visible card itself (the outer PopupBorder, which Margin
        // positions), so it lines up with the button's edges rather than its content.
        var inset2 = 2 * SortPopup.ContentInsetDip;
        if (openUp)
        {
            var rowCount = options.Count + (allowRemove ? 1 : 0);
            Margin = new Thickness(cardLeftDip, UpMarginTop(anchorVEdgeDip, EstimateHeight(rowCount), gapDip), 0, 0);
            EventHandler? onSized = null;
            onSized = (_, _) =>
            {
                if (content.Height <= 0)
                {
                    return;
                }

                Margin = new Thickness(cardLeftDip, UpMarginTop(anchorVEdgeDip, content.Height + inset2, gapDip), 0, 0);
                content.SizeChanged -= onSized;
            };
            content.SizeChanged += onSized;
        }
        else
        {
            Margin = new Thickness(cardLeftDip, Math.Max(anchorVEdgeDip + gapDip, MinTopDip), 0, 0);
        }
    }

    // Top margin so the card's bottom lands gapDip above the button's top.
    private static double UpMarginTop(double anchorTopDip, double cardHeightDip, double gapDip)
        => Math.Max(anchorTopDip - cardHeightDip - gapDip, MinTopDip);
    private static BoxView Divider() => new()
    {
        HeightRequest = 1,
        Color = Res<Color>("Gray600"),
        Opacity = 0.3,
    };

    private Border BuildRow(string key, string text, string glyph, Color color, Brush background)
    {
        var icon = new Image
        {
            WidthRequest = 18,
            HeightRequest = 18,
            VerticalOptions = LayoutOptions.Center,
            Source = new FontImageSource
            {
                Glyph = glyph,
                FontFamily = FluentIconsRegular.FontFamily,
                Color = color,
                Size = 18,
            },
        };
        Grid.SetColumn(icon, 0);

        var label = new Label
        {
            Text = text,
            Style = Res<Style>("Body2"),
            TextColor = color,
            VerticalTextAlignment = TextAlignment.Center,
        };
        Grid.SetColumn(label, 1);

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 10,
        };
        grid.Add(icon);
        grid.Add(label);

        var border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 0,
            Padding = new Thickness(10, 10),
            Background = background,
            Content = grid,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await CloseAsync(key);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private static T Res<T>(string key) => (T)Application.Current!.Resources[key];
}
