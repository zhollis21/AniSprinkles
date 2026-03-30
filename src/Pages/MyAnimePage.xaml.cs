using AniSprinkles.Utilities;
using IconFont.Maui.FluentIcons;

namespace AniSprinkles.Pages;

public partial class MyAnimePage : ContentPage
{
    private static readonly TimeSpan DeferredLoadDelay = TimeSpan.FromMilliseconds(120);

    private MyAnimePageModel? _viewModel;
    private bool _hasAppeared;
    private bool _hasCreatedLoadedContent;
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

        UpdateViewModeIcon(_viewModel.CurrentViewMode);

        // Content survived the flyout switch — just refresh data in background.
        if (LoadedContentHost.Content is not null)
        {
            await _viewModel.LoadAsync();
            return;
        }

        // Content needs to be (re)created.
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
        if (_viewModel?.IsAuthenticated == true && !_viewModel.IsErrorState && !_hasCreatedLoadedContent)
        {
            var view = new Views.MyAnimeLoadedContentView
            {
                BindingContext = _viewModel
            };

            LoadedContentHost.Content = view;
            _hasCreatedLoadedContent = true;
        }
        else if ((_viewModel?.IsAuthenticated != true || _viewModel.IsErrorState) && _hasCreatedLoadedContent)
        {
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
        // Create the loaded content view when authentication succeeds OR when
        // sections are populated. Gating on Sections.Count > 0 prevents premature
        // creation during LoadAsync (which sets IsAuthenticated before fetching data),
        // avoiding the triple-spinner problem (center spinner + SfPullToRefresh + EmptyView).
        if ((e.PropertyName is nameof(MyAnimePageModel.IsAuthenticated)
                or nameof(MyAnimePageModel.Sections)
                or nameof(MyAnimePageModel.IsErrorState))
            && _hasAppeared
            && _viewModel?.IsAuthenticated == true
            && _viewModel.Sections.Count > 0)
        {
            UpdateLoadedContentHost();
        }
        else if (e.PropertyName == nameof(MyAnimePageModel.IsErrorState)
            && _hasAppeared
            && _viewModel?.IsErrorState == true)
        {
            // Tear down loaded content so the error view is visible.
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
