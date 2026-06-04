using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using IconFont.Maui.FluentIcons;
using Microsoft.Maui.Controls.Shapes;

namespace AniSprinkles.Views;

/// <summary>
/// A compact sort picker (#85) that floats just beneath (or above) the tapped <see cref="SortDropdown"/>,
/// AniList-web style. CommunityToolkit.Maui 14.x has no popup anchor API and ignores VerticalOptions/
/// HorizontalOptions on the popup root (it centers), so we instead fill the screen with a transparent
/// scrim and absolutely position the card inside it from coordinates the caller measured off the trigger
/// (see <see cref="SortDropdown.OnTapped"/>). That also gives us reliable tap-to-dismiss: a tap on the
/// scrim closes with no result. Returns the chosen <see cref="SortOption.Code"/>, or <c>null</c> when
/// dismissed; it never mutates selection itself, so a failed server sort can revert the highlight upstream.
/// </summary>
public sealed class SortPopup : Popup<string?>
{
    public const double CardWidth = 210;

    public static async Task<string?> ShowAsync(
        string title,
        IReadOnlyList<SortOption> options,
        double screenWidthDip,
        double screenHeightDip,
        double cardTopDip,
        double cardRightDip,
        CancellationToken cancellationToken = default)
    {
        if (Shell.Current?.CurrentPage is not { } page)
        {
            return null;
        }

        var popup = new SortPopup(title, options, screenWidthDip, screenHeightDip, cardTopDip, cardRightDip);
        var popupOptions = new PopupOptions
        {
            Shape = null,
            Shadow = null,
            // We own the scrim + dismiss inside the full-screen content, so disable the toolkit's.
            CanBeDismissedByTappingOutsideOfPopup = false,
            PageOverlayColor = Colors.Transparent,
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
        string title,
        IReadOnlyList<SortOption> options,
        double screenWidthDip,
        double screenHeightDip,
        double cardTopDip,
        double cardRightDip)
    {
        BackgroundColor = Colors.Transparent;
        Padding = 0;
        Margin = 0;

        var content = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 6) };
        content.Add(new Label
        {
            Text = title,
            Style = Res<Style>("Caption2"),
            FontAttributes = FontAttributes.Bold,
            TextColor = Res<Color>("Gray400"),
            Margin = new Thickness(16, 6, 16, 6),
        });
        content.Add(new BoxView { HeightRequest = 1, Color = Res<Color>("Gray600"), Opacity = 0.6 });
        foreach (var option in options)
        {
            content.Add(BuildRow(option));
        }

        var card = new Border
        {
            StrokeThickness = 1,
            Stroke = Res<Color>("Gray600"),
            // Set Background (Brush), not BackgroundColor: the app's implicit Border style sets Background,
            // and Brush wins over Color, so a BackgroundColor here would be silently overridden.
            Background = new SolidColorBrush(Res<Color>("SecondaryBackground")),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = 0,
            WidthRequest = CardWidth,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, cardTopDip, cardRightDip, 0),
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.45f, Radius = 18, Offset = new Point(0, 4) },
            Content = content,
        };
        // Swallow taps on the card's non-row areas so they don't reach the scrim's dismiss handler.
        card.GestureRecognizers.Add(new TapGestureRecognizer());

        // Full-screen transparent scrim: provides the dim, owns placement, and dismisses on outside tap.
        var scrim = new Grid
        {
            WidthRequest = screenWidthDip,
            HeightRequest = screenHeightDip,
            BackgroundColor = Color.FromArgb("#59000000"),
        };
        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += async (_, _) => await CloseAsync(null);
        scrim.GestureRecognizers.Add(dismiss);
        scrim.Add(card);

        Content = scrim;
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
            // Transparent fill so the card shows through — must be a Brush (see card comment).
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
