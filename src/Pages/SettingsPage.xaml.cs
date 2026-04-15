using AniSprinkles.PageModels;

namespace AniSprinkles.Pages;

public partial class SettingsPage : ContentPage
{
    private static readonly TimeSpan DeferredLoadDelay = TimeSpan.FromMilliseconds(120);

    private SettingsPageModel? _viewModel;
    private bool _hasAppeared;
    private bool _hasCreatedLoadedContent;
    private int _loadVersion;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsPage(SettingsPageModel viewModel)
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

        // Content survived the flyout switch — just refresh data in background.
        if (LoadedContentHost.Content is not null)
        {
            await _viewModel.LoadAsync();
            return;
        }

        // Content needs to be (re)created. Reset tracking flag.
        _hasCreatedLoadedContent = false;

        int version;

        // Fast path: the singleton ViewModel already has cached profile data
        // from a previous visit. We still defer view creation so the Shell
        // transition animation completes first (InitializeComponent of the
        // heavy content view blocks the UI thread), but we skip the API call.
        // Flip CurrentState to InitialLoading during the delay so the spinner
        // is visible instead of a blank page.
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
        // content view. During this deferred delay, the current StateContainer
        // view remains visible until LoadAsync updates the page state.
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
            LoadedContentHost.Content = new Views.SettingsLoadedContentView
            {
                BindingContext = _viewModel
            };
            _hasCreatedLoadedContent = true;
        }
        else if (_viewModel?.IsAuthenticated != true && _hasCreatedLoadedContent)
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

        var services = Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services
            ?? Application.Current?.Handler?.MauiContext?.Services;
        var viewModel = services?.GetService<SettingsPageModel>();
        if (viewModel is null)
        {
            return;
        }

        SetViewModel(viewModel);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Create the loaded content view only when CurrentState == Content. Gating
        // on Content (not just IsAuthenticated) keeps the heavy XAML InitializeComponent
        // off the UI thread while CurrentState == InitialLoading — OnAppearing flips
        // to InitialLoading during the defer delay.
        if ((e.PropertyName is nameof(SettingsPageModel.IsAuthenticated)
                or nameof(SettingsPageModel.CurrentState))
            && _hasAppeared
            && _viewModel?.CurrentState == PageState.Content)
        {
            UpdateLoadedContentHost();
        }
        else if (e.PropertyName == nameof(SettingsPageModel.IsAuthenticated)
            && _hasAppeared
            && _viewModel?.IsAuthenticated != true)
        {
            // Tear down loaded content when the user signs out.
            UpdateLoadedContentHost();
        }
        else if (e.PropertyName == nameof(SettingsPageModel.CurrentState)
            && _hasAppeared
            && _viewModel?.CurrentState != PageState.Content
            && _hasCreatedLoadedContent)
        {
            // Tear down loaded content when CurrentState leaves Content
            // (e.g., auth succeeded but API call failed with no cached data).
            UpdateLoadedContentHost();
        }
    }

    private void SetViewModel(SettingsPageModel viewModel)
    {
        _viewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BindingContext = viewModel;
    }
}
