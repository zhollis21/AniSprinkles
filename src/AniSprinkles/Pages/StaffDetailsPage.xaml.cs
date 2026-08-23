using System.ComponentModel;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class StaffDetailsPage : ContentPage, IQueryAttributable
{
    private StaffDetailsPageModel ViewModel { get; }
    private ILogger<StaffDetailsPage> Logger { get; }
    private readonly DeferredContentLoader _loader;
    private int _pendingStaffId;

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

        _loader = new DeferredContentLoader(
            logger,
            LoadedContentHost,
            entityName: "staff",
            shouldShowContent: () => ViewModel.HasStaff && !ViewModel.IsBusy && ViewModel.CurrentState == PageState.Content,
            createView: () => new Views.StaffDetailsLoadedContentView { BindingContext = ViewModel },
            onRenderError: ex => ViewModel.ShowError(
                "Something Went Wrong",
                "Failed to render the staff view.",
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
        var staffId = QueryAttributeParser.ParseInt(query, "staffId");
        Logger.LogInformation("NAVTRACE StaffDetailsPage.ApplyQueryAttributes staffId={StaffId}", staffId);

        _loader.ResetContentIfStale(staffId != _pendingStaffId);
        _pendingStaffId = staffId;
        _loader.BumpVersion();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingStaffId));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loader.OnAppearing();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingStaffId));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _loader.OnDisappearing();
        // Abandon any in-flight fetches so a half-loaded page doesn't keep hitting the API after
        // the user has navigated away.
        ViewModel.CancelInFlight();
    }

    private async Task RunDeferredLoadAsync(int version, int staffId)
    {
        try
        {
            await Task.Yield();

            if (!_loader.IsCurrent(version))
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
            _loader.UpdateHost();
        }
    }
}
