using AniSprinkles.Converters;
using CommunityToolkit.Maui.Views;
using IconFont.Maui.FluentIcons;
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

    public MoveToListPopup(string animeTitle, MediaListStatus currentStatus)
    {
        InitializeComponent();
        TitleLabel.Text = animeTitle;
        BuildStatusRows(currentStatus);
    }

    private void BuildStatusRows(MediaListStatus currentStatus)
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
