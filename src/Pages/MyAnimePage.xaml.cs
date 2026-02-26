using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;
using IconFont.Maui.FluentIcons;

namespace AniSprinkles.Pages;

public partial class MyAnimePage : ContentPage
{
    private static readonly TimeSpan DeferredLoadDelay = TimeSpan.FromMilliseconds(120);

    private MyAnimePageModel? _viewModel;
    private ILogger<MyAnimePage>? _logger;
    private bool _hasAppeared;
    private bool _hasCreatedLoadedContent;
    private int _loadVersion;
    private CollectionView? _loadedCollectionView;

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

        UpdateViewModeIcon(_viewModel.CurrentViewMode);

        // Content survived the flyout switch — just refresh data in background.
        if (LoadedContentHost.Content is not null)
        {
            await _viewModel.LoadAsync();
            return;
        }

        // Content needs to be (re)created. Clean up stale references.
        if (_loadedCollectionView is not null)
        {
            _loadedCollectionView.SelectionChanged -= OnSelectionChanged;
            _loadedCollectionView = null;
        }
        _hasCreatedLoadedContent = false;

        int version;

        // Fast path: the singleton ViewModel already has cached sections from a
        // previous visit. We still defer view creation so the Shell transition
        // animation completes first (InitializeComponent of the heavy content
        // view blocks the UI thread), but we skip the API call.
        // IsRebuilding keeps the spinner visible during the delay so the user
        // sees a spinner instead of a blank page.
        if (_viewModel.HasLoadedData)
        {
            _viewModel.IsRebuilding = true;
            version = ++_loadVersion;
            await Task.Yield();
            await Task.Delay(DeferredLoadDelay);

            if (!_hasAppeared || version != _loadVersion)
            {
                _viewModel.IsRebuilding = false;
                return;
            }

            UpdateLoadedContentHost();
            _viewModel.IsRebuilding = false;
            // Background refresh with existing data visible.
            await _viewModel.LoadAsync();
            return;
        }

        // Slow path (first load): yield so the Shell transition animation can
        // complete before we run the data fetch and create the heavy XAML
        // content view. The XAML-bound loading overlay will be visible.
        version = ++_loadVersion;
        await Task.Yield();
        await Task.Delay(DeferredLoadDelay);

        if (!_hasAppeared || version != _loadVersion)
        {
            return;
        }

        await _viewModel.LoadAsync();
        UpdateLoadedContentHost();
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

    private void UpdateLoadedContentHost()
    {
        if (_viewModel?.IsAuthenticated == true && !_hasCreatedLoadedContent)
        {
            var view = new Views.MyAnimeLoadedContentView
            {
                BindingContext = _viewModel
            };

            // Wire up the CollectionView's SelectionChanged event from the loaded content view.
            var collectionView = view.FindByName<CollectionView>("AnimeCollectionView");
            if (collectionView is not null)
            {
                collectionView.SelectionChanged += OnSelectionChanged;
                _loadedCollectionView = collectionView;
            }

            LoadedContentHost.Content = view;
            _hasCreatedLoadedContent = true;
        }
        else if (_viewModel?.IsAuthenticated != true && _hasCreatedLoadedContent)
        {
            if (_loadedCollectionView is not null)
            {
                _loadedCollectionView.SelectionChanged -= OnSelectionChanged;
                _loadedCollectionView = null;
            }
            LoadedContentHost.Content = null;
            _hasCreatedLoadedContent = false;
        }
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
            EnsureLogger();
            var mediaId = entry.MediaId != 0 ? entry.MediaId : entry.Media?.Id ?? 0;
            _logger?.LogError(ex, "Navigation to media details failed for media entry {MediaId}", mediaId);
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
            // Logger not available
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Create the loaded content view when authentication succeeds OR when
        // sections are populated. Gating on Sections.Count > 0 prevents premature
        // creation during LoadAsync (which sets IsAuthenticated before fetching data),
        // avoiding the triple-spinner problem (center spinner + SfPullToRefresh + EmptyView).
        if ((e.PropertyName == nameof(MyAnimePageModel.IsAuthenticated)
                || e.PropertyName == nameof(MyAnimePageModel.Sections))
            && _hasAppeared
            && _viewModel?.IsAuthenticated == true
            && _viewModel.Sections.Count > 0)
        {
            UpdateLoadedContentHost();
        }
        else if (e.PropertyName == nameof(MyAnimePageModel.ViewModeIconGlyph) && _viewModel is not null)
        {
            UpdateViewModeIcon(_viewModel.CurrentViewMode);
        }
    }

    private void UpdateViewModeIcon(ListViewMode mode)
    {
        var glyph = mode switch
        {
            ListViewMode.Large => FluentIconsRegular.Grid24,
            ListViewMode.Compact => FluentIconsRegular.TextBulletListSquare24,
            _ => FluentIconsRegular.List24,
        };

        ViewModeIcon.Glyph = glyph;
    }

    private void SetViewModel(MyAnimePageModel viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BindingContext = viewModel;
    }
}
