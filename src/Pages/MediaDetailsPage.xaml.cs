using System.Diagnostics;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.Pages;

public partial class MediaDetailsPage : ContentPage, IQueryAttributable
{
    private MediaDetailsPageModel ViewModel { get; }
    private ILogger<MediaDetailsPage> Logger { get; }
    private string _activeNavTraceId = "none";
    private DateTimeOffset? _activeNavStartUtc;
    private bool _hasCreatedLoadedContent;
    private bool _hasAppeared;
    private int _pendingMediaId;
    private MediaListEntry? _pendingListEntry;
    private int _pendingQueryVersion;
    private int _scheduledQueryVersion;

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
        LoadedContentHost.Content = null;
        _hasCreatedLoadedContent = false;

        _activeNavTraceId = NavigationTelemetryHelper.ParseTraceId(query);
        _activeNavStartUtc = NavigationTelemetryHelper.ParseNavigationStart(query);
        var mediaId = 0;
        if (query.TryGetValue("mediaId", out var rawId))
        {
            if (rawId is int id)
            {
                mediaId = id;
            }
            else if (rawId is string text && int.TryParse(text, out var parsed))
            {
                mediaId = parsed;
            }
        }

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

        // Queue requested media; actual load starts after the page has appeared and yielded a frame.
        // This keeps Shell transition animation smooth instead of competing with immediate details work.
        _pendingMediaId = mediaId;
        _pendingListEntry = entry;
        _pendingQueryVersion++;
        TryScheduleDeferredLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _hasAppeared = true;
        UpdateLoadedContentHost();
        Logger.LogInformation(
            "NAVTRACE {TraceId} MediaDetailsPage.OnAppearing at {NowUtc:O} (+{SinceTapMs}ms)",
            _activeNavTraceId,
            DateTimeOffset.UtcNow,
            NavigationTelemetryHelper.GetElapsedFromTapMilliseconds(_activeNavStartUtc));
        TryScheduleDeferredLoad();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hasAppeared = false;
        _pendingQueryVersion++;
        // Reset deferred content so each details navigation starts with a minimal page tree.
        LoadedContentHost.Content = null;
        _hasCreatedLoadedContent = false;
    }

    private void TryScheduleDeferredLoad()
    {
        if (!_hasAppeared || _pendingQueryVersion == _scheduledQueryVersion)
        {
            return;
        }

        var queryVersion = _pendingQueryVersion;
        _scheduledQueryVersion = queryVersion;
        var mediaId = _pendingMediaId;
        var entry = _pendingListEntry;
        var navTraceId = _activeNavTraceId;
        var navStartUtc = _activeNavStartUtc;

        RunDeferredLoadAsync(queryVersion, mediaId, entry, navTraceId, navStartUtc)
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        Logger.LogError(
                            task.Exception,
                            "NAVTRACE {TraceId} deferred load task faulted for media {MediaId}",
                            navTraceId,
                            mediaId);
                    }
                },
                TaskScheduler.FromCurrentSynchronizationContext());
    }

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

            if (!_hasAppeared || queryVersion != _pendingQueryVersion)
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
        if (e.PropertyName is nameof(MediaDetailsPageModel.IsBusy) or nameof(MediaDetailsPageModel.HasMedia))
        {
            UpdateLoadedContentHost();
        }
    }

    private void UpdateLoadedContentHost()
    {
        if (ViewModel.HasMedia && !ViewModel.IsBusy)
        {
            if (!_hasCreatedLoadedContent)
            {
                // Keep first navigation frame lightweight: create the heavy details subtree only after
                // data has loaded, so the user sees the loading page instantly.
                LoadedContentHost.Content = new Views.MediaDetailsLoadedContentView
                {
                    BindingContext = ViewModel
                };
                _hasCreatedLoadedContent = true;
            }
        }
        else if (_hasCreatedLoadedContent)
        {
            LoadedContentHost.Content = null;
            _hasCreatedLoadedContent = false;
        }
    }
}
