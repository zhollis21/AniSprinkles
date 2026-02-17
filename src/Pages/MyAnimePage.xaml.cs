using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.Pages;

public partial class MyAnimePage : ContentPage
{
    private static readonly TimeSpan DeferredLoadDelay = TimeSpan.FromMilliseconds(120);

    private MyAnimePageModel? _viewModel;
    private ILogger<MyAnimePage>? _logger;
    private bool _hasAppeared;
    private int _loadVersion;

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
        _hasAppeared = true;
        EnsureViewModel();
        if (_viewModel is null)
        {
            return;
        }

        // Let Shell transition animation complete before loading heavy data.
        var version = ++_loadVersion;
        await Task.Yield();
        await Task.Delay(DeferredLoadDelay);

        if (!_hasAppeared || version != _loadVersion)
        {
            return;
        }

        await _viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hasAppeared = false;
        _loadVersion++;
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

        // Clear selection immediately in same frame as navigation to avoid visual artifacts.
        // This prevents the selected item from remaining highlighted while transitioning.
        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        try
        {
            await _viewModel.OpenDetailsCommand.ExecuteAsync(entry);
        }
        catch (Exception ex)
        {
            // Log the exception for debugging and crash analysis
            EnsureLogger();
            var mediaId = entry.MediaId != 0 ? entry.MediaId : entry.Media?.Id ?? 0;
            _logger?.LogError(ex, "Navigation to media details failed for media entry {MediaId}", mediaId);

            // Inform user via UI
            _viewModel.StatusMessage = "Navigation failed. Please try again.";
        }
    }

    private void EnsureViewModel()
    {
        if (_viewModel is not null)
        {
            return;
        }

        try
        {
            var services = ServiceProviderHelper.GetServiceProvider();
            var viewModel = services?.GetService<MyAnimePageModel>();
            if (viewModel is null)
            {
                return;
            }

            SetViewModel(viewModel);
        }
        catch (InvalidOperationException)
        {
            // Service provider not available yet (early startup on Android)
            // This is normal during Shell page initialization
            return;
        }
    }

    private void EnsureLogger()
    {
        if (_logger is not null)
        {
            return;
        }

        try
        {
            var services = ServiceProviderHelper.GetServiceProvider();
            _logger = services?.GetService<ILogger<MyAnimePage>>();
        }
        catch (InvalidOperationException)
        {
            // Logger not available, logging will be skipped
        }
    }

    private void SetViewModel(MyAnimePageModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
    }
}
