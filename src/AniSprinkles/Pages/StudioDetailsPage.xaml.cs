using System.ComponentModel;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class StudioDetailsPage : ContentPage, IQueryAttributable
{
    private StudioDetailsPageModel ViewModel { get; }
    private ILogger<StudioDetailsPage> Logger { get; }
    private readonly DeferredContentLoader _loader;
    private int _pendingStudioId;

    public StudioDetailsPage()
        : this(
            ServiceProviderHelper.GetServiceProvider().GetRequiredService<StudioDetailsPageModel>(),
            ServiceProviderHelper.GetServiceProvider().GetRequiredService<ILogger<StudioDetailsPage>>())
    {
    }

    public StudioDetailsPage(StudioDetailsPageModel viewModel, ILogger<StudioDetailsPage> logger)
    {
        InitializeComponent();
        ViewModel = viewModel;
        Logger = logger;
        BindingContext = ViewModel;

        _loader = new DeferredContentLoader(
            logger,
            LoadedContentHost,
            entityName: "studio",
            shouldShowContent: () => ViewModel.HasStudio && !ViewModel.IsBusy && ViewModel.CurrentState == PageState.Content,
            createView: () => new Views.StudioDetailsLoadedContentView { BindingContext = ViewModel },
            onRenderError: ex => ViewModel.ShowError(
                "Something Went Wrong",
                "Failed to render the studio view.",
                canRetry: true,
                details: $"{ex.GetType().Name}: {ex.Message}",
                iconGlyph: FluentIconsRegular.ErrorCircle24));

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        var studioId = QueryAttributeParser.ParseInt(query, "studioId");
        Logger.LogInformation("NAVTRACE StudioDetailsPage.ApplyQueryAttributes studioId={StudioId}", studioId);

        _loader.ResetContentIfStale(studioId != _pendingStudioId);
        _pendingStudioId = studioId;
        _loader.BumpVersion();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingStudioId));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loader.OnAppearing();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingStudioId));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _loader.OnDisappearing();
        ViewModel.CancelInFlight();
    }

    private async Task RunDeferredLoadAsync(int version, int studioId)
    {
        try
        {
            await Task.Yield();

            if (!_loader.IsCurrent(version))
            {
                return;
            }

            await ViewModel.LoadAsync(studioId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "StudioDetailsPage load failed for studio {StudioId}", studioId);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StudioDetailsPageModel.IsBusy)
            or nameof(StudioDetailsPageModel.HasStudio)
            or nameof(StudioDetailsPageModel.CurrentState))
        {
            _loader.UpdateHost();
        }
    }
}
