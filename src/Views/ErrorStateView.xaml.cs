using System.Windows.Input;
using IconFont.Maui.FluentIcons;

namespace AniSprinkles.Views;

public partial class ErrorStateView : ContentView
{
    public static readonly BindableProperty ErrorTitleProperty =
        BindableProperty.Create(nameof(ErrorTitle), typeof(string), typeof(ErrorStateView), "Something Went Wrong");

    public static readonly BindableProperty ErrorSubtitleProperty =
        BindableProperty.Create(nameof(ErrorSubtitle), typeof(string), typeof(ErrorStateView), "An unexpected error occurred. Try again or check back later.");

    public static readonly BindableProperty ErrorIconGlyphProperty =
        BindableProperty.Create(nameof(ErrorIconGlyph), typeof(string), typeof(ErrorStateView), FluentIconsRegular.ErrorCircle24);

    public static readonly BindableProperty ErrorAccentColorProperty =
        BindableProperty.Create(nameof(ErrorAccentColor), typeof(Color), typeof(ErrorStateView), Colors.OrangeRed);

    public static readonly BindableProperty RetryCommandProperty =
        BindableProperty.Create(nameof(RetryCommand), typeof(ICommand), typeof(ErrorStateView));

    public static readonly BindableProperty ShowRetryButtonProperty =
        BindableProperty.Create(nameof(ShowRetryButton), typeof(bool), typeof(ErrorStateView), true);

    public static readonly BindableProperty ErrorDetailsProperty =
        BindableProperty.Create(nameof(ErrorDetails), typeof(string), typeof(ErrorStateView), string.Empty,
            propertyChanged: (b, _, _) => ((ErrorStateView)b).OnPropertyChanged(nameof(HasErrorDetails)));

    public static readonly BindableProperty IsDetailsExpandedProperty =
        BindableProperty.Create(nameof(IsDetailsExpanded), typeof(bool), typeof(ErrorStateView), false,
            propertyChanged: (b, _, _) => ((ErrorStateView)b).OnPropertyChanged(nameof(DetailsToggleText)));

    public string ErrorTitle
    {
        get => (string)GetValue(ErrorTitleProperty);
        set => SetValue(ErrorTitleProperty, value);
    }

    public string ErrorSubtitle
    {
        get => (string)GetValue(ErrorSubtitleProperty);
        set => SetValue(ErrorSubtitleProperty, value);
    }

    public string ErrorIconGlyph
    {
        get => (string)GetValue(ErrorIconGlyphProperty);
        set => SetValue(ErrorIconGlyphProperty, value);
    }

    public Color ErrorAccentColor
    {
        get => (Color)GetValue(ErrorAccentColorProperty);
        set => SetValue(ErrorAccentColorProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public string ErrorDetails
    {
        get => (string)GetValue(ErrorDetailsProperty);
        set => SetValue(ErrorDetailsProperty, value);
    }

    public bool IsDetailsExpanded
    {
        get => (bool)GetValue(IsDetailsExpandedProperty);
        set => SetValue(IsDetailsExpandedProperty, value);
    }

    public bool ShowRetryButton
    {
        get => (bool)GetValue(ShowRetryButtonProperty);
        set => SetValue(ShowRetryButtonProperty, value);
    }

    public bool HasRetryCommand => RetryCommand is not null && ShowRetryButton;

    public bool HasErrorDetails => !string.IsNullOrWhiteSpace(ErrorDetails);

    public string DetailsToggleText => IsDetailsExpanded ? "Hide technical details" : "Show technical details";

    public ErrorStateView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName is nameof(RetryCommand) or nameof(ShowRetryButton))
        {
            OnPropertyChanged(nameof(HasRetryCommand));
        }
    }

    private void OnToggleDetailsTapped(object? sender, EventArgs e)
    {
        IsDetailsExpanded = !IsDetailsExpanded;
    }

    private async void OnCopyTapped(object? sender, EventArgs e)
    {
        if (HasErrorDetails)
        {
            await Clipboard.Default.SetTextAsync(ErrorDetails);
        }
    }

    private async void OnShareTapped(object? sender, EventArgs e)
    {
        if (HasErrorDetails)
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Text = ErrorDetails,
                Title = "AniSprinkles Error Details"
            });
        }
    }
}
