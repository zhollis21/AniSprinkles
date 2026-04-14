namespace AniSprinkles.Views;

public partial class FormatIconBadge : ContentView
{
    public static readonly BindableProperty FormatProperty =
        BindableProperty.Create(nameof(Format), typeof(string), typeof(FormatIconBadge));

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(FormatIconBadge), 18.0);

    public string? Format
    {
        get => (string?)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public FormatIconBadge()
    {
        InitializeComponent();
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.End;
        Margin = new Thickness(6, 0, 0, 6);
    }
}
