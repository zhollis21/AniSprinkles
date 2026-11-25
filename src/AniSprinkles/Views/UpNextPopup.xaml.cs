using AniSprinkles.Utilities;
using CommunityToolkit.Maui.Views;

namespace AniSprinkles.Views;

/// <summary>
/// The "Up next" bottom sheet (#103): a just-completed entry's eligible relations, each with where it
/// will go and a check. Closes with an <see cref="UpNextChoice"/>, or null for Not now.
/// </summary>
public partial class UpNextPopup : Popup<object>
{
    // At most this much of the screen goes to the list; the rest stays for the header and buttons.
    private const double MaxListHeightFraction = 0.5;

    private readonly UpNextPickerView _picker;

    public UpNextPopup(string completedTitle, IReadOnlyList<UpNextCandidate> candidates)
    {
        InitializeComponent();
        SubtitleLabel.Text = $"You finished {completedTitle}";

        _picker = new UpNextPickerView(candidates);
        _picker.SelectionChanged += (_, _) => UpdateAddButton();
        PickerScroll.Content = _picker;
        UpdateAddButton();

        // Anchor as a bottom sheet — see MoveToListPopup for why each of these is set here.
        Padding = new Thickness(0);
        VerticalOptions = LayoutOptions.End;
        HorizontalOptions = LayoutOptions.Center;
        Margin = new Thickness(0, 1, 0, 0);

        var screen = ScreenMetrics.Read();
        SheetBorder.WidthRequest = screen.WidthDip;
        PickerScroll.MaximumHeightRequest = screen.HeightDip * MaxListHeightFraction;

        Opened += OnOpenedGoEdgeToEdge;
    }

    private void UpdateAddButton()
    {
        var enabled = _picker.Selected.Count > 0;
        AddButton.Text = _picker.ConfirmText;
        AddButton.IsEnabled = enabled;
        // Filled in the app's accent while there is something to do; a flat grey slab, reading "Select
        // titles", while there isn't, so it never looks tappable when tapping would do nothing.
        AddButton.Background = new SolidColorBrush(enabled ? Resource("Primary", Colors.Cyan) : Resource("Gray600", Colors.Gray));
        AddButton.TextColor = enabled ? Resource("PrimaryDarkText", Colors.Black) : Resource("Gray400", Colors.Gray);
    }

    private static Color Resource(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : fallback;

    // Not now closes with a value that is not an UpNextChoice, which the dialog service reads as null.
    private static readonly object NotNow = new();

    private async void OnNotNowClicked(object? sender, EventArgs e) => await CloseAsync(NotNow);

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (_picker.ToChoice() is { } choice)
        {
            await CloseAsync(choice);
        }
    }

    private void OnOpenedGoEdgeToEdge(object? sender, EventArgs e)
    {
        Opened -= OnOpenedGoEdgeToEdge;
        BottomSheetChrome.GoEdgeToEdge(this, SheetContent);
    }
}
