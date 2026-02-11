using System.Diagnostics;
using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class MediaDetailsPage : ContentPage, IQueryAttributable
{
    private static readonly TimeSpan DeferredInitialLoadDelay = TimeSpan.FromMilliseconds(120);

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
        : this(ResolveViewModel(), ResolveLogger())
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

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        LoadedContentHost.Content = null;
        _hasCreatedLoadedContent = false;

        _activeNavTraceId = ParseTraceId(query);
        _activeNavStartUtc = ParseNavigationStart(query);
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
            GetElapsedFromTapMilliseconds(_activeNavStartUtc));

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
            GetElapsedFromTapMilliseconds(_activeNavStartUtc));
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

        _ = RunDeferredLoadAsync(queryVersion, mediaId, entry, navTraceId, navStartUtc);
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
            await Task.Delay(DeferredInitialLoadDelay);

            if (!_hasAppeared || queryVersion != _pendingQueryVersion)
            {
                return;
            }

            Logger.LogInformation(
                "NAVTRACE {TraceId} deferred details load dispatch at {NowUtc:O} (+{SinceTapMs}ms)",
                navTraceId,
                DateTimeOffset.UtcNow,
                GetElapsedFromTapMilliseconds(navStartUtc));

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
                GetElapsedFromTapMilliseconds(navStartUtc));

            await ViewModel.LoadAsync(mediaId, entry);

            stopwatch.Stop();
            Logger.LogInformation(
                "NAVTRACE {TraceId} details load finished in {ElapsedMs}ms (+{SinceTapMs}ms)",
                navTraceId,
                stopwatch.ElapsedMilliseconds,
                GetElapsedFromTapMilliseconds(navStartUtc));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "NAVTRACE {TraceId} details load failed for media {MediaId}", navTraceId, mediaId);
        }
    }

    private static MediaDetailsPageModel ResolveViewModel()
    {
        // Route materialization can occur before Handler wiring is complete after activity restarts.
        // Use platform services fallback to avoid null service-provider lookups during early page creation.
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services;
        if (services is null)
        {
            throw new InvalidOperationException("Service provider not available.");
        }

        return services.GetRequiredService<MediaDetailsPageModel>();
    }

    private static ILogger<MediaDetailsPage> ResolveLogger()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services;
        if (services is null)
        {
            throw new InvalidOperationException("Service provider not available.");
        }

        return services.GetRequiredService<ILogger<MediaDetailsPage>>();
    }

    private static string ParseTraceId(IDictionary<string, object> query)
    {
        if (query.TryGetValue("navTraceId", out var rawTraceId) && rawTraceId is string traceId && !string.IsNullOrWhiteSpace(traceId))
        {
            return traceId;
        }

        return "unknown";
    }

    private static DateTimeOffset? ParseNavigationStart(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("navStartUtcTicks", out var rawStart))
        {
            return null;
        }

        return rawStart switch
        {
            long ticks => new DateTimeOffset(ticks, TimeSpan.Zero),
            int ticks => new DateTimeOffset(ticks, TimeSpan.Zero),
            string text when long.TryParse(text, out var parsedTicks) => new DateTimeOffset(parsedTicks, TimeSpan.Zero),
            _ => null
        };
    }

    private static long GetElapsedFromTapMilliseconds(DateTimeOffset? navigationStartUtc)
    {
        if (!navigationStartUtc.HasValue)
        {
            return -1;
        }

        return Math.Max((long)(DateTimeOffset.UtcNow - navigationStartUtc.Value).TotalMilliseconds, 0);
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
