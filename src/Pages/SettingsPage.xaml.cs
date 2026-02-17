namespace AniSprinkles.Pages;

public partial class SettingsPage : ContentPage
{
    private static readonly TimeSpan DeferredLoadDelay = TimeSpan.FromMilliseconds(120);

    private SettingsPageModel? _viewModel;
    private bool _hasAppeared;
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
        var viewModel = services?.GetService<SettingsPageModel>();
        if (viewModel is null)
        {
            return;
        }

        SetViewModel(viewModel);
    }

    private void SetViewModel(SettingsPageModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
    }
}
