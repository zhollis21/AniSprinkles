using AniSprinkles.PageModels;
using AniSprinkles.Utilities;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class MyAnimePage : ContentPage
{
    private static readonly TimeSpan DeferredLoadDelay = TimeSpan.FromMilliseconds(120);

    private MyAnimePageModel? _viewModel;
    private bool _hasAppeared;
    private bool _hasCreatedLoadedContent;
    private int _loadVersion;
    private readonly ToolbarItem? _searchToolbarItem;
    private readonly ToolbarItem? _viewModeToolbarItem;
    private readonly ILogger<MyAnimePage>? _logger;

    public MyAnimePage()
    {
        InitializeComponent();
        // Stash toolbar items so we can add/remove them based on auth state.
        _searchToolbarItem = SearchToolbarItem;
        _viewModeToolbarItem = ViewModeToolbarItem;

        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<MyAnimePage>();
        }
        catch (InvalidOperationException)
        {
        }
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
        UpdateToolbarItems();

        // Content survived the flyout switch — just refresh data in background.
        if (LoadedContentHost.Content is not null)
        {
            await _viewModel.LoadAsync();
            // Tear down loaded content if the user signed out while away.
            UpdateLoadedContentHost();
            return;
        }

        // Content needs to be (re)created.
        _hasCreatedLoadedContent = false;

        int version;

        // Fast path: the singleton ViewModel already has cached sections from a
        // previous visit. We still defer view creation so the Shell transition
        // animation completes first (InitializeComponent of the heavy content
        // view blocks the UI thread), but we skip the API call. Flip CurrentState
        // to InitialLoading during the delay so the spinner is visible instead of
        // a blank page.
        if (_viewModel.HasLoadedData)
        {
            var savedState = _viewModel.CurrentState;
            _viewModel.CurrentState = PageState.InitialLoading;
            version = ++_loadVersion;
            await Task.Yield();
            await Task.Delay(DeferredLoadDelay);

            if (!_hasAppeared || version != _loadVersion)
            {
                // Abort: only restore state if we're still the one showing the spinner.
                if (_viewModel.CurrentState == PageState.InitialLoading)
                {
                    _viewModel.CurrentState = savedState;
                }
                return;
            }

            _viewModel.CurrentState = PageState.Content;
            UpdateLoadedContentHost();
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
        var isError = _viewModel?.CurrentState == PageState.Error;
        var isAuth = _viewModel?.IsAuthenticated == true;

        if (isAuth && !isError && !_hasCreatedLoadedContent)
        {
            var view = new Views.MyAnimeLoadedContentView
            {
                BindingContext = _viewModel
            };

            _logger?.LogInformation(
                "LOADEDHOST MyAnime attach (isAuth={IsAuth}, isError={IsError}, currentState={CurrentState})",
                isAuth, isError, _viewModel?.CurrentState);
            LoadedContentHost.Content = view;
            _hasCreatedLoadedContent = true;
        }
        else if ((!isAuth || isError) && _hasCreatedLoadedContent)
        {
            _logger?.LogInformation(
                "LOADEDHOST MyAnime detach (isAuth={IsAuth}, isError={IsError}, currentState={CurrentState})",
                isAuth, isError, _viewModel?.CurrentState);
            LoadedContentHost.Content = null;
            _hasCreatedLoadedContent = false;
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

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Create the loaded content view only when CurrentState == Content. Gating
        // on Content (not just "not Error") keeps the heavy XAML InitializeComponent
        // off the UI thread while CurrentState == InitialLoading — OnAppearing flips
        // to InitialLoading during the defer delay, and we don't want the view
        // materialized until the Shell transition animation has finished.
        if ((e.PropertyName is nameof(MyAnimePageModel.IsAuthenticated)
                or nameof(MyAnimePageModel.Sections)
                or nameof(MyAnimePageModel.CurrentState))
            && _hasAppeared
            && _viewModel?.IsAuthenticated == true
            && _viewModel.CurrentState == PageState.Content
            && _viewModel.Sections.Count > 0)
        {
            UpdateLoadedContentHost();
            UpdateToolbarItems();
        }
        else if (e.PropertyName == nameof(MyAnimePageModel.CurrentState)
            && _hasAppeared
            && _viewModel?.CurrentState == PageState.Error)
        {
            // Tear down loaded content so the error view is visible.
            UpdateLoadedContentHost();
        }
        else if (e.PropertyName == nameof(MyAnimePageModel.IsAuthenticated)
            && _hasAppeared
            && _viewModel?.IsAuthenticated != true)
        {
            // Tear down loaded content when the user signs out.
            UpdateLoadedContentHost();
            UpdateToolbarItems();
        }
        else if (e.PropertyName == nameof(MyAnimePageModel.ViewModeIconGlyph) && _viewModel is not null)
        {
            UpdateViewModeIcon(_viewModel.CurrentViewMode);
        }
    }

    private void UpdateToolbarItems()
    {
        if (_searchToolbarItem is null || _viewModeToolbarItem is null)
        {
            return;
        }

        bool authenticated = _viewModel?.IsAuthenticated == true;
        bool hasSearch = ToolbarItems.Contains(_searchToolbarItem);

        if (authenticated && !hasSearch)
        {
            ToolbarItems.Add(_searchToolbarItem);
            ToolbarItems.Add(_viewModeToolbarItem);
        }
        else if (!authenticated && hasSearch)
        {
            ToolbarItems.Remove(_searchToolbarItem);
            ToolbarItems.Remove(_viewModeToolbarItem);
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
        _viewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BindingContext = viewModel;
    }
}
