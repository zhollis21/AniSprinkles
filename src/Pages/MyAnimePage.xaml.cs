namespace AniSprinkles.Pages;

public partial class MyAnimePage : ContentPage
{
    private MyAnimePageModel? _viewModel;

    public MyAnimePage()
    {
        InitializeComponent();
    }

    public MyAnimePage(MyAnimePageModel viewModel)
        : this()
    {
        SetViewModel(viewModel);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureViewModel();
        if (_viewModel is null)
        {
            return;
        }

        await _viewModel.LoadAsync();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        EnsureViewModel();
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MediaListEntry entry)
        {
            return;
        }

        EnsureViewModel();
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            await _viewModel.OpenDetailsCommand.ExecuteAsync(entry);
        }
        finally
        {
            if (sender is CollectionView collectionView)
            {
                // Clearing selection can trigger a list visual update; defer it until after navigation is underway
                // so we do not spend tap-to-route budget re-rendering the source list.
                collectionView.SelectedItem = null;
            }
        }
    }

    private void EnsureViewModel()
    {
        if (_viewModel is not null)
        {
            return;
        }

        // Shell can build flyout pages before Application.Current.Handler is ready.
        // Resolve lazily from whichever service provider is available at handler/appearance time.
        var services = Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services
            ?? Application.Current?.Handler?.MauiContext?.Services;
        var viewModel = services?.GetService<MyAnimePageModel>();
        if (viewModel is null)
        {
            return;
        }

        SetViewModel(viewModel);
    }

    private void SetViewModel(MyAnimePageModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
    }
}
