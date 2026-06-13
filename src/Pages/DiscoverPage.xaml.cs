using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class DiscoverPage : ContentPage
{
    private static readonly TimeSpan DeferredLoadDelay = TimeSpan.FromMilliseconds(120);

    private DiscoverPageModel? _viewModel;
    private bool _hasAppeared;
    private bool _hasCreatedLoadedContent;
    private int _loadVersion;
    private readonly ILogger<DiscoverPage>? _logger;

    public DiscoverPage()
    {
        InitializeComponent();

        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<DiscoverPage>();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public DiscoverPage(DiscoverPageModel viewModel)
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

        // Content survived the flyout switch — LoadAsync is a no-op within the TTL,
        // a background refresh past it.
        if (LoadedContentHost.Content is not null)
        {
            await _viewModel.LoadAsync();
            UpdateLoadedContentHost();
            return;
        }

        _hasCreatedLoadedContent = false;

        int version;

        // Fast path: the singleton ViewModel already has cached sections. Defer view creation so
        // the Shell transition animation completes first (the content view's InitializeComponent
        // blocks the UI thread), but skip the API call when the TTL hasn't lapsed.
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
            // TTL-aware refresh with existing data visible.
            await _viewModel.LoadAsync();
            return;
        }

        // Slow path (first load): yield so the Shell transition animation can complete
        // before the data fetch and heavy content view creation.
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

        if (!isError && !_hasCreatedLoadedContent)
        {
            var view = new Views.DiscoverLoadedContentView
            {
                BindingContext = _viewModel
            };

            _logger?.LogInformation(
                "LOADEDHOST Discover attach (isError={IsError}, currentState={CurrentState})",
                isError, _viewModel?.CurrentState);
            LoadedContentHost.Content = view;
            _hasCreatedLoadedContent = true;
        }
        else if (isError && _hasCreatedLoadedContent)
        {
            _logger?.LogInformation(
                "LOADEDHOST Discover detach (currentState={CurrentState})",
                _viewModel?.CurrentState);
            HandlerHelper.DisconnectAll(LoadedContentHost.Content);
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
            var viewModel = services?.GetService<DiscoverPageModel>();
            if (viewModel is null)
            {
                return;
            }

            SetViewModel(viewModel);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Create the loaded content view only when CurrentState == Content (not during
        // InitialLoading) so the heavy InitializeComponent stays off the UI thread until
        // the Shell transition animation has finished. Tear it down on Error so the
        // full-page error view is visible.
        if (e.PropertyName != nameof(DiscoverPageModel.CurrentState) || !_hasAppeared)
        {
            return;
        }

        if (_viewModel?.CurrentState is PageState.Content or PageState.Error)
        {
            UpdateLoadedContentHost();
        }
    }

    private void SetViewModel(DiscoverPageModel viewModel)
    {
        _viewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BindingContext = viewModel;
    }
}
