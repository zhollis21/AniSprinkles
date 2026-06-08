using System.ComponentModel;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class StudioDetailsPage : ContentPage, IQueryAttributable
{
    private StudioDetailsPageModel ViewModel { get; }
    private ILogger<StudioDetailsPage> Logger { get; }
    private bool _hasCreatedLoadedContent;
    private bool _hasAppeared;
    private int _pendingStudioId;
    private int _pendingQueryVersion;
    private int _scheduledQueryVersion;

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
        var studioId = 0;
        if (query.TryGetValue("studioId", out var raw))
        {
            if (raw is int id)
            {
                studioId = id;
            }
            else if (raw is string text && int.TryParse(text, out var parsed))
            {
                studioId = parsed;
            }
        }

        Logger.LogInformation("NAVTRACE StudioDetailsPage.ApplyQueryAttributes studioId={StudioId}", studioId);

        if (studioId != _pendingStudioId || !_hasCreatedLoadedContent)
        {
            HandlerHelper.DisconnectAll(LoadedContentHost.Content);
            LoadedContentHost.Content = null;
            _hasCreatedLoadedContent = false;
        }

        _pendingStudioId = studioId;
        _pendingQueryVersion++;
        TryScheduleDeferredLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _hasAppeared = true;
        UpdateLoadedContentHost();
        TryScheduleDeferredLoad();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hasAppeared = false;
        _pendingQueryVersion++;
        ViewModel.CancelInFlight();
    }

    private void TryScheduleDeferredLoad()
    {
        if (!_hasAppeared || _pendingQueryVersion == _scheduledQueryVersion)
        {
            return;
        }

        var version = _pendingQueryVersion;
        var studioId = _pendingStudioId;
        _scheduledQueryVersion = version;

        RunDeferredLoadAsync(version, studioId)
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        Logger.LogError(task.Exception, "StudioDetailsPage deferred load faulted for studio {StudioId}", studioId);
                    }
                },
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task RunDeferredLoadAsync(int version, int studioId)
    {
        try
        {
            await Task.Yield();

            if (!_hasAppeared || version != _pendingQueryVersion)
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
            UpdateLoadedContentHost();
        }
    }

    private void UpdateLoadedContentHost()
    {
        if (ViewModel.HasStudio && !ViewModel.IsBusy && ViewModel.CurrentState == PageState.Content)
        {
            if (!_hasCreatedLoadedContent)
            {
                Logger.LogInformation(
                    "LOADEDHOST StudioDetails attach (hasStudio={HasStudio}, isBusy={IsBusy}, currentState={CurrentState})",
                    ViewModel.HasStudio, ViewModel.IsBusy, ViewModel.CurrentState);
                try
                {
                    LoadedContentHost.Content = new Views.StudioDetailsLoadedContentView
                    {
                        BindingContext = ViewModel
                    };
                    _hasCreatedLoadedContent = true;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to create StudioDetailsLoadedContentView");
                    ViewModel.ErrorTitle = "Something Went Wrong";
                    ViewModel.ErrorSubtitle = "Failed to render the studio view.";
                    ViewModel.ErrorIconGlyph = FluentIconsRegular.ErrorCircle24;
                    ViewModel.ErrorDetails = $"{ex.GetType().Name}: {ex.Message}";
                    ViewModel.CanRetry = true;
                    ViewModel.CurrentState = PageState.Error;
                }
            }
        }
        else if (_hasCreatedLoadedContent)
        {
            Logger.LogInformation(
                "LOADEDHOST StudioDetails detach (hasStudio={HasStudio}, isBusy={IsBusy}, currentState={CurrentState})",
                ViewModel.HasStudio, ViewModel.IsBusy, ViewModel.CurrentState);
            HandlerHelper.DisconnectAll(LoadedContentHost.Content);
            LoadedContentHost.Content = null;
            _hasCreatedLoadedContent = false;
        }
    }
}
