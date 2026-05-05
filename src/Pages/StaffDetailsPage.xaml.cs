using System.ComponentModel;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.Pages;

public partial class StaffDetailsPage : ContentPage, IQueryAttributable
{
    private StaffDetailsPageModel ViewModel { get; }
    private ILogger<StaffDetailsPage> Logger { get; }
    private bool _hasCreatedLoadedContent;
    private bool _hasAppeared;
    private int _pendingStaffId;
    private int _pendingQueryVersion;
    private int _scheduledQueryVersion;

    public StaffDetailsPage()
        : this(
            ServiceProviderHelper.GetServiceProvider().GetRequiredService<StaffDetailsPageModel>(),
            ServiceProviderHelper.GetServiceProvider().GetRequiredService<ILogger<StaffDetailsPage>>())
    {
    }

    public StaffDetailsPage(StaffDetailsPageModel viewModel, ILogger<StaffDetailsPage> logger)
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
        var staffId = 0;
        if (query.TryGetValue("staffId", out var raw))
        {
            if (raw is int id)
            {
                staffId = id;
            }
            else if (raw is string text && int.TryParse(text, out var parsed))
            {
                staffId = parsed;
            }
        }

        Logger.LogInformation("NAVTRACE StaffDetailsPage.ApplyQueryAttributes staffId={StaffId}", staffId);

        if (staffId != _pendingStaffId || !_hasCreatedLoadedContent)
        {
            HandlerHelper.DisconnectAll(LoadedContentHost.Content);
            LoadedContentHost.Content = null;
            _hasCreatedLoadedContent = false;
        }

        _pendingStaffId = staffId;
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
    }

    private void TryScheduleDeferredLoad()
    {
        if (!_hasAppeared || _pendingQueryVersion == _scheduledQueryVersion)
        {
            return;
        }

        var version = _pendingQueryVersion;
        var staffId = _pendingStaffId;
        _scheduledQueryVersion = version;

        RunDeferredLoadAsync(version, staffId)
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        Logger.LogError(task.Exception, "StaffDetailsPage deferred load faulted for staff {StaffId}", staffId);
                    }
                },
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task RunDeferredLoadAsync(int version, int staffId)
    {
        try
        {
            await Task.Yield();

            if (!_hasAppeared || version != _pendingQueryVersion)
            {
                return;
            }

            await ViewModel.LoadAsync(staffId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "StaffDetailsPage load failed for staff {StaffId}", staffId);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StaffDetailsPageModel.IsBusy)
            or nameof(StaffDetailsPageModel.HasStaff)
            or nameof(StaffDetailsPageModel.CurrentState))
        {
            UpdateLoadedContentHost();
        }
    }

    private void UpdateLoadedContentHost()
    {
        if (ViewModel.HasStaff && !ViewModel.IsBusy && ViewModel.CurrentState == PageState.Content)
        {
            if (!_hasCreatedLoadedContent)
            {
                try
                {
                    LoadedContentHost.Content = new Views.StaffDetailsLoadedContentView
                    {
                        BindingContext = ViewModel
                    };
                    _hasCreatedLoadedContent = true;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to create StaffDetailsLoadedContentView");
                    ViewModel.ErrorTitle = "Something Went Wrong";
                    ViewModel.ErrorSubtitle = "Failed to render the staff view.";
                    ViewModel.ErrorIconGlyph = FluentIconsRegular.ErrorCircle24;
                    ViewModel.ErrorDetails = $"{ex.GetType().Name}: {ex.Message}";
                    ViewModel.CanRetry = true;
                    ViewModel.CurrentState = PageState.Error;
                }
            }
        }
        else if (_hasCreatedLoadedContent)
        {
            HandlerHelper.DisconnectAll(LoadedContentHost.Content);
            LoadedContentHost.Content = null;
            _hasCreatedLoadedContent = false;
        }
    }
}
