using AniSprinkles.Utilities;
using IconFont.Maui.FluentIcons;

namespace AniSprinkles.Views;

public partial class CoverImage : ContentView
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(CoverImage), propertyChanged: OnSourceChanged);

    public static readonly BindableProperty PlaceholderGlyphProperty =
        BindableProperty.Create(nameof(PlaceholderGlyph), typeof(string), typeof(CoverImage), FluentIconsRegular.Person48);

    public static readonly BindableProperty FormatProperty =
        BindableProperty.Create(nameof(Format), typeof(string), typeof(CoverImage));

    public static readonly BindableProperty HasRealImageProperty =
        BindableProperty.Create(nameof(HasRealImage), typeof(bool), typeof(CoverImage), false);

    public static readonly BindableProperty ListStatusTextProperty =
        BindableProperty.Create(nameof(ListStatusText), typeof(string), typeof(CoverImage));

    public static readonly BindableProperty ListStatusColorProperty =
        BindableProperty.Create(nameof(ListStatusColor), typeof(Color), typeof(CoverImage), Colors.Transparent);

    /// <summary>The image URL. A null/empty value, or AniList's "default.jpg" no-image URL, shows the placeholder.</summary>
    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>FluentIcons glyph for the placeholder; defaults to a person silhouette. Media covers pass a media glyph.</summary>
    public string PlaceholderGlyph
    {
        get => (string)GetValue(PlaceholderGlyphProperty);
        set => SetValue(PlaceholderGlyphProperty, value);
    }

    /// <summary>When set, overlays the <see cref="FormatIconBadge"/> (media covers only).</summary>
    public string? Format
    {
        get => (string?)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public bool HasRealImage
    {
        get => (bool)GetValue(HasRealImageProperty);
        private set => SetValue(HasRealImageProperty, value);
    }

    /// <summary>When non-empty, overlays the on-list status pill (top-left) — e.g. "Watching". Blank hides it.</summary>
    public string? ListStatusText
    {
        get => (string?)GetValue(ListStatusTextProperty);
        set => SetValue(ListStatusTextProperty, value);
    }

    /// <summary>Fill color for the status pill, keyed to <see cref="MediaListStatus"/> (see RelatedMedia.ListStatusColor).</summary>
    public Color ListStatusColor
    {
        get => (Color)GetValue(ListStatusColorProperty);
        set => SetValue(ListStatusColorProperty, value);
    }

    public CoverImage()
    {
        InitializeComponent();
    }

    private static void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
        => ((CoverImage)bindable).HasRealImage = ImageUrl.IsReal(newValue as string);
}
