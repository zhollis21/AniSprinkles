using System.Collections;
using System.Windows.Input;

namespace AniSprinkles.Views;

/// <summary>
/// One Discover section: accent header (with View All) + horizontal infinite cover-card carousel.
/// All inputs are bindable properties rather than BindingContext so the parent view can stamp
/// one instance per <c>DiscoverRow</c> from a single template.
/// </summary>
public partial class DiscoverSectionView : ContentView
{
    public static readonly BindableProperty HeaderTextProperty =
        BindableProperty.Create(nameof(HeaderText), typeof(string), typeof(DiscoverSectionView), string.Empty);

    public static readonly BindableProperty HeaderGlyphProperty =
        BindableProperty.Create(nameof(HeaderGlyph), typeof(string), typeof(DiscoverSectionView), string.Empty);

    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(DiscoverSectionView));

    public static readonly BindableProperty NavigateCommandProperty =
        BindableProperty.Create(nameof(NavigateCommand), typeof(ICommand), typeof(DiscoverSectionView));

    public static readonly BindableProperty ViewAllCommandProperty =
        BindableProperty.Create(nameof(ViewAllCommand), typeof(ICommand), typeof(DiscoverSectionView));

    public static readonly BindableProperty SectionKeyProperty =
        BindableProperty.Create(nameof(SectionKey), typeof(string), typeof(DiscoverSectionView), string.Empty);

    public static readonly BindableProperty LongPressCommandProperty =
        BindableProperty.Create(nameof(LongPressCommand), typeof(ICommand), typeof(DiscoverSectionView));

    public static readonly BindableProperty LoadMoreCommandProperty =
        BindableProperty.Create(nameof(LoadMoreCommand), typeof(ICommand), typeof(DiscoverSectionView));

    public static readonly BindableProperty IsLoadingMoreProperty =
        BindableProperty.Create(nameof(IsLoadingMore), typeof(bool), typeof(DiscoverSectionView), false);

    public DiscoverSectionView()
    {
        InitializeComponent();
        CollectionViewLongPress.Attach(Carousel, item =>
        {
            if (LongPressCommand?.CanExecute(item) == true)
            {
                LongPressCommand.Execute(item);
            }
        });
    }

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public string HeaderGlyph
    {
        get => (string)GetValue(HeaderGlyphProperty);
        set => SetValue(HeaderGlyphProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    public ICommand? ViewAllCommand
    {
        get => (ICommand?)GetValue(ViewAllCommandProperty);
        set => SetValue(ViewAllCommandProperty, value);
    }

    /// <summary>The <c>DiscoverSection</c> enum name passed to <see cref="ViewAllCommand"/>.</summary>
    public string SectionKey
    {
        get => (string)GetValue(SectionKeyProperty);
        set => SetValue(SectionKeyProperty, value);
    }

    /// <summary>Invoked with the long-pressed <c>BrowseMediaItem</c> (the entry-action menu).</summary>
    public ICommand? LongPressCommand
    {
        get => (ICommand?)GetValue(LongPressCommandProperty);
        set => SetValue(LongPressCommandProperty, value);
    }

    /// <summary>Invoked with <see cref="SectionKey"/> when the carousel nears its end (row infinite scroll).</summary>
    public ICommand? LoadMoreCommand
    {
        get => (ICommand?)GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    /// <summary>Shows the carousel's trailing Load More spinner.</summary>
    public bool IsLoadingMore
    {
        get => (bool)GetValue(IsLoadingMoreProperty);
        set => SetValue(IsLoadingMoreProperty, value);
    }
}
