using System.ComponentModel;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class MediaBrowsePage : ContentPage, IQueryAttributable
{
    private MediaBrowsePageModel ViewModel { get; }
    private ILogger<MediaBrowsePage> Logger { get; }
    private readonly DeferredContentLoader _loader;
    private DiscoverSection? _pendingSection;

    public MediaBrowsePage()
        : this(
            ServiceProviderHelper.GetServiceProvider().GetRequiredService<MediaBrowsePageModel>(),
            ServiceProviderHelper.GetServiceProvider().GetRequiredService<ILogger<MediaBrowsePage>>())
    {
    }

    public MediaBrowsePage(MediaBrowsePageModel viewModel, ILogger<MediaBrowsePage> logger)
    {
        InitializeComponent();
        ViewModel = viewModel;
        Logger = logger;
        BindingContext = ViewModel;

        _loader = new DeferredContentLoader(
            logger,
            LoadedContentHost,
            entityName: "browse",
            shouldShowContent: () => ViewModel.HasSection && !ViewModel.IsBusy && ViewModel.CurrentState == PageState.Content,
            createView: () => new Views.MediaBrowseLoadedContentView { BindingContext = ViewModel },
            onRenderError: ex =>
            {
                ViewModel.ErrorTitle = "Something Went Wrong";
                ViewModel.ErrorSubtitle = "Failed to render the browse list.";
                ViewModel.ErrorIconGlyph = FluentIconsRegular.ErrorCircle24;
                ViewModel.ErrorDetails = $"{ex.GetType().Name}: {ex.Message}";
                ViewModel.CanRetry = true;
                ViewModel.CurrentState = PageState.Error;
            });

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateViewModeIcon(ViewModel.CurrentViewMode);
    }

    private void UpdateViewModeIcon(ListViewMode mode)
    {
        ViewModeIcon.Glyph = mode switch
        {
            ListViewMode.Large => FluentIconsRegular.Grid24,
            ListViewMode.Compact => FluentIconsRegular.TextBulletListSquare24,
            _ => FluentIconsRegular.List24,
        };
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        DiscoverSection? section = null;
        if (query.TryGetValue("section", out var raw)
            && Enum.TryParse<DiscoverSection>(raw?.ToString(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            // IsDefined rejects numeric strings ("999" parses to an undefined enum value), which
            // would otherwise throw in DiscoverSectionDefinitions.Get(); a null section falls
            // through to the page model's "Unknown browse section" error.
            section = parsed;
        }

        Logger.LogInformation("NAVTRACE MediaBrowsePage.ApplyQueryAttributes section={Section}", section);

        _loader.ResetContentIfStale(section != _pendingSection);
        _pendingSection = section;
        _loader.BumpVersion();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingSection));
    }


    // Display settings can change while this page sits in a backgrounded tab's stack. OnAppearing
    // does not fire when that tab becomes current again — only OnNavigatedTo does — so the
    // re-projection hangs off this rather than off the load path (#127; see AGENTS.md).
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel.RefreshDisplaySettings();
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loader.OnAppearing();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingSection));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _loader.OnDisappearing();
        ViewModel.CancelInFlight();
    }

    private async Task RunDeferredLoadAsync(int version, DiscoverSection? section)
    {
        try
        {
            await Task.Yield();

            if (!_loader.IsCurrent(version))
            {
                return;
            }

            await ViewModel.LoadAsync(section);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "MediaBrowsePage load failed for section {Section}", section);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaBrowsePageModel.IsBusy)
            or nameof(MediaBrowsePageModel.PageTitle)
            or nameof(MediaBrowsePageModel.CurrentState))
        {
            _loader.UpdateHost();
        }
        else if (e.PropertyName == nameof(MediaBrowsePageModel.CurrentViewMode))
        {
            UpdateViewModeIcon(ViewModel.CurrentViewMode);
        }
    }
}
