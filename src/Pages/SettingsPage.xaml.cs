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
        // IsLoading=true keeps the spinner visible during the delay so the user
        // sees a spinner instead of a blank page.
        if (_viewModel.HasLoadedData)
        {
            _viewModel.IsLoading = true;
            version = ++_loadVersion;
            await Task.Yield();
            await Task.Delay(DeferredLoadDelay);

            if (!_hasAppeared || version != _loadVersion)
            {
                _viewModel.IsLoading = false;
                return;
            }

            UpdateLoadedContentHost();
            _viewModel.IsLoading = false;
            // Background refresh with existing data visible.
            await _viewModel.LoadAsync();
            return;
        }

        // Slow path (first load): ensure the spinner is visible for the very
        // first frame. The ViewModel defaults IsLoading=true, but if this is a
        // return visit on the same singleton it may have been set to false.
        _viewModel.IsLoading = true;

        // Yield so the Shell transition animation can complete before we run
        // the data fetch (which will create the heavy XAML content view).
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
        if (e.PropertyName == nameof(SettingsPageModel.IsAuthenticated) && _hasAppeared)
        {
            UpdateLoadedContentHost();
        }
    }

    private void SetViewModel(SettingsPageModel viewModel)
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
