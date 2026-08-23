using System.ComponentModel;
using System.Diagnostics;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class MediaDetailsPage : ContentPage, IQueryAttributable
{
    private MediaDetailsPageModel ViewModel { get; }
    private ILogger<MediaDetailsPage> Logger { get; }
    private readonly DeferredContentLoader _loader;
    private string _activeNavTraceId = "none";
    private DateTimeOffset? _activeNavStartUtc;
    private int _pendingMediaId;
    private MediaListEntry? _pendingListEntry;

    public MediaDetailsPage()
        : this(
            ServiceProviderHelper.GetServiceProvider()!.GetRequiredService<MediaDetailsPageModel>(),
            ServiceProviderHelper.GetServiceProvider()!.GetRequiredService<ILogger<MediaDetailsPage>>()
        )
    {
    }

    public MediaDetailsPage(MediaDetailsPageModel viewModel, ILogger<MediaDetailsPage> logger)
    {
        InitializeComponent();
        ViewModel = viewModel;
        Logger = logger;
        BindingContext = ViewModel;

        _loader = new DeferredContentLoader(
            logger,
            LoadedContentHost,
            entityName: "media",
            shouldShowContent: () => ViewModel.HasMedia && !ViewModel.IsBusy && ViewModel.CurrentState == PageState.Content,
            createView: () => new Views.MediaDetailsLoadedContentView { BindingContext = ViewModel },
            onRenderError: ex => ViewModel.ShowError(
                "Something Went Wrong",
                "Failed to render the details view.",
                canRetry: true,
                details: $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                iconGlyph: FluentIconsRegular.ErrorCircle24));

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            // Cleanup when the page handler is removed (page destroyed on Android)
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _activeNavTraceId = NavigationTelemetryHelper.ParseTraceId(query);
        _activeNavStartUtc = NavigationTelemetryHelper.ParseNavigationStart(query);
        var mediaId = QueryAttributeParser.ParseInt(query, "mediaId");

        MediaListEntry? entry = null;
        if (query.TryGetValue("listEntry", out var rawEntry) && rawEntry is MediaListEntry castEntry)
        {
            entry = castEntry;
            if (mediaId == 0)
            {
                mediaId = entry.MediaId != 0 ? entry.MediaId : entry.Media?.Id ?? 0;
            }
        }

        Logger.LogInformation(
            "NAVTRACE {TraceId} ApplyQueryAttributes received media {MediaId} at {NowUtc:O} (+{SinceTapMs}ms)",
            _activeNavTraceId,
            mediaId,
            DateTimeOffset.UtcNow,
            NavigationTelemetryHelper.GetElapsedFromTapMilliseconds(_activeNavStartUtc));

        // Only tear down the heavy content view when navigating to a different media. On back
        // navigation Shell re-applies the same query attributes, so keeping the existing view avoids
        // a costly XAML re-inflation that causes a multi-second hang.
        _loader.ResetContentIfStale(mediaId != _pendingMediaId);

        // Queue requested media; actual load starts after the page has appeared and yielded a frame.
        // This keeps the Shell transition animation smooth instead of competing with details work.
        _pendingMediaId = mediaId;
        _pendingListEntry = entry;
        _loader.BumpVersion();
        ScheduleLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loader.OnAppearing();
        Logger.LogInformation(
            "NAVTRACE {TraceId} MediaDetailsPage.OnAppearing at {NowUtc:O} (+{SinceTapMs}ms)",
            _activeNavTraceId,
            DateTimeOffset.UtcNow,
            NavigationTelemetryHelper.GetElapsedFromTapMilliseconds(_activeNavStartUtc));
        ScheduleLoad();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _loader.OnDisappearing();
        // Abandon any in-flight load / section fetch so we don't keep hitting the API after navigating
        // away. List ops recreate the scope via EnsurePageScope, so the sort popup's OnDisappearing
        // stays harmless.
        ViewModel.CancelInFlight();
    }

    private void ScheduleLoad()
        => _loader.TrySchedule(version => RunDeferredLoadAsync(
            version, _pendingMediaId, _pendingListEntry, _activeNavTraceId, _activeNavStartUtc));

    private async Task RunDeferredLoadAsync(
        int queryVersion,
        int mediaId,
        MediaListEntry? entry,
        string navTraceId,
        DateTimeOffset? navStartUtc)
    {
        try
        {
            await Task.Yield();

            if (!_loader.IsCurrent(queryVersion))
            {
                return;
            }

            Logger.LogInformation(
                "NAVTRACE {TraceId} deferred details load dispatch at {NowUtc:O} (+{SinceTapMs}ms)",
                navTraceId,
                DateTimeOffset.UtcNow,
                NavigationTelemetryHelper.GetElapsedFromTapMilliseconds(navStartUtc));

            await LoadWithTraceAsync(mediaId, entry, navTraceId, navStartUtc);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "NAVTRACE {TraceId} deferred details load scheduling failed for media {MediaId}", navTraceId, mediaId);
        }
    }

    private async Task LoadWithTraceAsync(
        int mediaId,
        MediaListEntry? entry,
        string navTraceId,
        DateTimeOffset? navStartUtc)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            Logger.LogInformation(
                "NAVTRACE {TraceId} details load start media {MediaId} (+{SinceTapMs}ms)",
                navTraceId,
                mediaId,
                NavigationTelemetryHelper.GetElapsedFromTapMilliseconds(navStartUtc));

            await ViewModel.LoadAsync(mediaId, entry);

            stopwatch.Stop();
            Logger.LogInformation(
                "NAVTRACE {TraceId} details load finished in {ElapsedMs}ms (+{SinceTapMs}ms)",
                navTraceId,
                stopwatch.ElapsedMilliseconds,
                NavigationTelemetryHelper.GetElapsedFromTapMilliseconds(navStartUtc));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "NAVTRACE {TraceId} details load failed for media {MediaId}", navTraceId, mediaId);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaDetailsPageModel.IsBusy)
            or nameof(MediaDetailsPageModel.HasMedia)
            or nameof(MediaDetailsPageModel.CurrentState))
        {
            _loader.UpdateHost();
        }
    }
}
