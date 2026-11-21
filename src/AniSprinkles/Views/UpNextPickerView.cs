using AniSprinkles.Utilities;
using Microsoft.Maui.Controls.Shapes;

namespace AniSprinkles.Views;

/// <summary>
/// The Up next sheet's list (#103): one row per related title, each saying where it will go, with a
/// round check. Built in code, like <see cref="MoveToListPopup"/>'s rows: the row count is only known
/// at runtime and each row is a handful of elements. The wording comes from Core
/// (<see cref="UpNextCandidate.DestinationLabel"/>, <see cref="UpNextCandidates.ConfirmLabel"/>),
/// where it is tested.
/// <para>
/// There is no list switch: rows can go to different lists (an unreleased title to Planning, a
/// completed one to Rewatching), and the list they follow otherwise is a Settings choice already.
/// Every row starts unticked: the sheet suggests, and nothing is added without a tap on it.
/// </para>
/// </summary>
public sealed class UpNextPickerView : ContentView
{
    private const double ChipAlpha = 0.18;
    private const double SelectedRowAlpha = 0.10;

    private readonly List<Row> _rows = [];

    public UpNextPickerView(IReadOnlyList<UpNextCandidate> candidates)
    {
        var rows = new VerticalStackLayout { Spacing = 2 };
        foreach (var candidate in candidates)
        {
            rows.Children.Add(BuildRow(candidate));
        }

        Content = rows;
    }

    /// <summary>Raised when a row is ticked or unticked, so the host can relabel its button.</summary>
    public event EventHandler? SelectionChanged;

    public IReadOnlyList<UpNextCandidate> Selected => [.. _rows.Where(r => r.IsSelected).Select(r => r.Candidate)];

    /// <summary>"Add 2 · Move 1", or "Select titles" when nothing is ticked.</summary>
    public string ConfirmText => UpNextCandidates.ConfirmLabel(Selected);

    public UpNextChoice? ToChoice() => Selected is { Count: > 0 } selected ? new UpNextChoice(selected) : null;

    private View BuildRow(UpNextCandidate candidate)
    {
        var cover = new Border
        {
            WidthRequest = 40,
            HeightRequest = 56,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Background = new SolidColorBrush(Resource("Gray600", Colors.Gray)),
            Content = candidate.Media.CoverImage?.Medium is { } coverUrl && ImageUrl.IsReal(coverUrl)
                // Sized as well as its Border: in an Auto grid column an unsized Image measures to the
                // bitmap's own aspect and came out as a 6dp sliver on device.
                ? new Image { Source = coverUrl, Aspect = Aspect.AspectFill, WidthRequest = 40, HeightRequest = 56 }
                : null,
        };

        var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        chips.Children.Add(Chip(candidate.RelationLabel, Resource("Primary", Colors.Cyan)));
        if (candidate.DetailsLine is { Length: > 0 } details)
        {
            chips.Children.Add(Chip(details, Resource("Gray300", Colors.LightGray), Resource("Gray600", Colors.Gray)));
        }

        // On every unreleased row, moves included: it is why such a row goes to Plan to Watch whatever
        // the Settings list says.
        if (candidate.IsUnreleased)
        {
            chips.Children.Add(Chip("Not yet released", Resource("RainbowOrange", Colors.Orange)));
        }

        var destination = candidate.DestinationLabel;
        chips.Children.Add(Chip(destination, Resource("RainbowGreen", Colors.Green)));

        var text = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        text.Children.Add(new Label
        {
            Text = candidate.Media.DisplayTitle,
            Style = Resource<Style>("Body2Strong"),
            MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation,
        });
        text.Children.Add(chips);

        var checkMark = new Image
        {
            WidthRequest = 14,
            HeightRequest = 14,
            Source = new FontImageSource
            {
                Glyph = FluentIconsRegular.Checkmark24,
                FontFamily = FluentIconsRegular.FontFamily,
                Color = Resource("PrimaryDarkText", Colors.Black),
                Size = 14,
            },
        };
        var check = new Border
        {
            WidthRequest = 24,
            HeightRequest = 24,
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            VerticalOptions = LayoutOptions.Center,
            Content = checkMark,
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            ],
            ColumnSpacing = 12,
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(check, 2);
        grid.Children.Add(cover);
        grid.Children.Add(text);
        grid.Children.Add(check);

        // The whole row is the tap target, highlighted while ticked.
        var container = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(8, 8),
            Content = grid,
        };

        var row = new Row(candidate, container, check, checkMark, destination);
        row.Apply(false);
        _rows.Add(row);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            row.Apply(!row.IsSelected);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        container.GestureRecognizers.Add(tap);
        return container;
    }

    private static Border Chip(string text, Color accent, Color? background = null)
    {
        var chip = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(7, 2),
            Margin = new Thickness(0, 0, 4, 4),
            Background = new SolidColorBrush((background ?? accent).WithAlpha(background is null ? (float)ChipAlpha : 1f)),
            Content = new Label { Text = text, FontSize = 11, TextColor = accent, LineBreakMode = LineBreakMode.NoWrap },
        };

        // A chip is one line: it wraps to the next row as a whole, never within itself. Without both of
        // these "TV · 2026" rendered as "TV ·" — FlexLayout shrinks children by default, and the label
        // then word-wrapped "2026" onto a second line the chip had no height for.
        FlexLayout.SetShrink(chip, 0);
        return chip;
    }

    private static Color Resource(string key, Color fallback) => Resource<Color>(key) ?? fallback;

    private static T? Resource<T>(string key) where T : class
        => Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as T : null;

    /// <summary>One row's state and the views that show it.</summary>
    private sealed class Row(UpNextCandidate candidate, Border container, Border check, Image checkMark, string destination)
    {
        public UpNextCandidate Candidate { get; } = candidate;

        public bool IsSelected { get; private set; }

        public void Apply(bool selected)
        {
            IsSelected = selected;
            var accent = Resource("Primary", Colors.Cyan);
            container.Background = selected
                ? new SolidColorBrush(accent.WithAlpha((float)SelectedRowAlpha))
                : new SolidColorBrush(Colors.Transparent);
            check.Stroke = selected ? accent : Resource("Gray500", Colors.Gray);
            check.Background = new SolidColorBrush(selected ? accent : Colors.Transparent);
            checkMark.IsVisible = selected;
            SemanticProperties.SetDescription(
                container,
                $"{Candidate.Media.DisplayTitle}, {destination}, {(selected ? "selected" : "not selected")}");
        }
    }
}
