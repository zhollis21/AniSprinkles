using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sentry;
using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

/// <summary>
/// The shared spine of the four details page models (media, character, staff, studio). Owns the
/// load/cancel lifecycle over <see cref="PageLoadScope"/>, the <see cref="PageState"/> transitions and
/// error-state population, retry, the optimistic favourite toggle, the external-link command, and
/// navigate-to-media. Each page model supplies its fetch and its own sections.
///
/// <para>The entity is reached through <see cref="Entity"/> rather than held here, so each page model
/// keeps its own <c>[ObservableProperty]</c> field — and with it every existing XAML binding and
/// <c>[NotifyPropertyChangedFor]</c> list.</para>
/// </summary>
/// <typeparam name="TEntity">The favouritable entity the page displays.</typeparam>
public abstract partial class DetailsPageModelBase<TEntity> : ObservableObject
    where TEntity : class, IFavouritable
{
    private readonly IAuthService _authService;
    private readonly IExternalBrowser _browser;
    private readonly ErrorReportService _errorReportService;
    private readonly FavouriteToggleRunner _favouriteRunner;

    protected DetailsPageModelBase(
        IAniListClient aniListClient,
        IAuthService authService,
        INavigationService navigationService,
        IUserFeedback feedback,
        IExternalBrowser browser,
        ErrorReportService errorReportService,
        ILogger logger)
    {
        AniList = aniListClient;
        _authService = authService;
        NavigationService = navigationService;
        Feedback = feedback;
        _browser = browser;
        _errorReportService = errorReportService;
        Logger = logger;
        ListOps = new ListOperationRunner(logger, feedback);
        _favouriteRunner = new FavouriteToggleRunner(aniListClient, feedback, logger);
    }

    protected IAniListClient AniList { get; }

    protected INavigationService NavigationService { get; }

    protected IUserFeedback Feedback { get; }

    protected ILogger Logger { get; }

    protected ListOperationRunner ListOps { get; }

    /// <summary>Page-lifetime cancellation scope; <see cref="CancelInFlight"/> aborts it.</summary>
    protected PageLoadScope Scope { get; } = new();

    /// <summary>The id being displayed; the section fetchers page against it.</summary>
    protected int LoadedId { get; private set; }

    /// <summary>The id <c>RetryLoad</c> re-invokes with.</summary>
    protected int LastRequestedId { get; private set; }

    /// <summary>Identifies the live load so a superseded one cannot clear <see cref="IsBusy"/> out from
    /// under it. Mirrors the generation guard in <see cref="PaginatedSection{T}"/>.</summary>
    private int _loadGeneration;

    // ---- State ------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.InitialLoading;

    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorSubtitle = string.Empty;

    [ObservableProperty]
    private string _errorIconGlyph = string.Empty;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    [ObservableProperty]
    private bool _canRetry = true;

    partial void OnCurrentStateChanged(PageState oldValue, PageState newValue)
        => Logger.LogInformation("PageState: {OldState} → {NewState} (key={StateKey})", oldValue, newValue, CurrentStateKey ?? "(null)");

    public bool IsAuthenticated { get; private set; }

    /// <summary>Viewer's favorite state for the displayed entity; drives the heart fill.</summary>
    public bool IsFavourite => Entity?.IsFavourite ?? false;

    /// <summary>Compact favourite count ("1.2k", "1.2M", empty for none).</summary>
    public string FavouritesDisplay => Entity?.FavouritesDisplay ?? string.Empty;

    public bool HasFavourites => Entity?.Favourites is > 0;

    public bool CanToggleFavourite => IsAuthenticated && !_favouriteRunner.IsBusy && Entity is not null;

    public bool HasSiteUrl => !string.IsNullOrWhiteSpace(SiteUrl);

    // ---- What each page supplies ------------------------------------------------------------------

    /// <summary>The displayed entity. Implemented against the page model's own observable field so its
    /// bindings and change notifications stay where they are.</summary>
    protected abstract TEntity? Entity { get; set; }

    /// <summary>Lower-case noun used in log lines, breadcrumbs and the default error copy.</summary>
    protected abstract string EntityNoun { get; }

    /// <summary>Prefix for this page's NAVTRACE lines, e.g. <c>StudioDetails</c>.</summary>
    protected abstract string TracePrefix { get; }

    /// <summary>Which AniList favourite field the toggle flips.</summary>
    protected abstract FavouriteKind FavouriteKind { get; }

    /// <summary>The entity's AniList URL, or null when it has none.</summary>
    protected abstract string? SiteUrl { get; }

    /// <summary>Fetches the entity. Returning null takes the not-found path.</summary>
    protected abstract Task<TEntity?> FetchAsync(int id, CancellationToken cancellationToken);

    /// <summary>Seeds the page's sections from the freshly fetched entity.</summary>
    protected abstract void SeedSections(TEntity entity);

    /// <summary>Clears sections, sort selections and any transient view state before a new entity loads.</summary>
    protected virtual void ResetForNewEntity() { }

    /// <summary>Section counts for the fetch+seed NAVTRACE line, e.g. "12 productions".</summary>
    protected virtual string DescribeSeededSections() => string.Empty;

    /// <summary>Raise any page-specific notifications a favourite flip affects.</summary>
    protected virtual void OnFavouriteChanged() { }

    /// <summary>Runs when a load is skipped because the same entity is already displayed.</summary>
    protected virtual void OnEntityReused() { }

    /// <summary>
    /// The carousel items on this page whose bound text is computed from a display setting (#127).
    /// Empty by default; a page with no such items has nothing to re-project.
    /// </summary>
    protected virtual IEnumerable<IDisplayProjection> DisplayProjections => [];

    /// <summary>
    /// Runs when a display setting moved while this page was alive but off-screen. Re-raise anything
    /// computed from one on the page model <em>itself</em> — the header title, the rating control —
    /// as distinct from the carousel items, which <see cref="DisplayProjections"/> covers.
    /// </summary>
    protected virtual void OnDisplaySettingsChanged() { }

    /// <summary>Runs once a load has committed to fetching, before the request goes out.</summary>
    protected virtual void OnLoadStarting(int id) { }

    /// <summary>Runs after <see cref="IsAuthenticated"/> is set, for anything else derived from it.</summary>
    protected virtual void OnAuthenticationResolved() { }

    /// <summary>
    /// The display settings this page's current content was rendered under (#127). Seeded at
    /// construction so a page model built after a change does not re-project content it never had.
    /// </summary>
    private DisplaySettingsSnapshot _renderedDisplaySettings = DisplaySettingsSnapshot.Current;

    /// <summary>
    /// Re-renders what is on screen when a display setting moved under it. No fetch, and no work at
    /// all when nothing moved — this runs on every appearance, including every sort-popup dismissal.
    /// </summary>
    private void ReprojectIfDisplaySettingsChanged()
    {
        var current = DisplaySettingsSnapshot.Current;
        if (!current.RenderingDiffersFrom(_renderedDisplaySettings))
        {
            return;
        }

        if (current.TitleLanguageDiffersFrom(_renderedDisplaySettings))
        {
            foreach (var item in DisplayProjections)
            {
                item.RefreshDisplayProjections();
            }
        }

        OnDisplaySettingsChanged();
        _renderedDisplaySettings = current;
    }

    /// <summary>Records that the content on screen matches the settings as they stand now.</summary>
    private protected void MarkDisplaySettingsRendered()
        => _renderedDisplaySettings = DisplaySettingsSnapshot.Current;

    /// <summary>
    /// Called from the page's <c>OnNavigatedTo</c>, which is what actually fires when this page's tab
    /// becomes current again (#127).
    /// <para>
    /// <b>The pushed-page lifecycle asymmetry, measured on device.</b> Detail pages and View All are
    /// pushed onto a tab's navigation stack, and MAUI Shell gives a page in a backgrounded stack no
    /// appearing/disappearing when the tab changes. The navigation hooks are a strict superset —
    /// they fire everywhere the appearing hooks do, <em>plus</em> the two tab-switch cases where
    /// those do not:
    /// </para>
    /// <code>
    /// path                        OnAppearing  OnDisappearing  OnNavigatedTo  OnNavigatedFrom
    /// initial navigation              yes            -              yes              -
    /// tab away (pushed page)           -            NO               -              yes
    /// tab back (pushed page)          NO             -              yes               -
    /// forward nav to sub-page          -            yes              -              yes
    /// back nav from sub-page          yes            -              yes               -
    /// sort popup open                  -            yes              -              yes
    /// sort popup close                yes            -              yes               -
    /// tab-root page (Settings)        yes           yes             yes              yes
    /// </code>
    /// <para>
    /// That is why the five pushed pages hang their <em>whole</em> lifecycle off
    /// <c>OnNavigatedTo</c>/<c>OnNavigatedFrom</c> (#132), not just this re-projection (#127). The
    /// two must stay paired: <c>DeferredContentLoader.OnDisappearing</c> clears <c>_hasAppeared</c>,
    /// which gates both <c>IsCurrent</c> and <c>TrySchedule</c>, so a disarm with no matching re-arm
    /// would latch the loader off after a single tab round-trip.
    /// </para>
    /// <para>
    /// Tab-root pages (Library, Discover, Search, Settings) get the full set and are unaffected —
    /// which is why <c>SettingsPage.OnDisappearing</c> still reliably flushes a pending save
    /// (#118, #128).
    /// </para>
    /// <para>
    /// Idempotent: it compares against the last rendered snapshot and does nothing when they match,
    /// so the extra call on the initial navigation costs nothing.
    /// </para>
    /// </summary>
    public void RefreshDisplaySettings() => ReprojectIfDisplaySettingsChanged();

    /// <summary>Whether a null fetch result should still offer Retry.</summary>
    protected virtual bool NullResultIsRetryable => false;

    protected virtual (string Title, string Subtitle) InvalidIdError
        => ("Not Found", $"Invalid {EntityNoun} id.");

    protected virtual (string Title, string Subtitle) NotFoundError
        => ("Not Found", $"We couldn't find this {EntityNoun}.");

    protected virtual (string Title, string Subtitle) FallbackLoadError
        => ("Something Went Wrong", $"Failed to load {EntityNoun} details.");

    // ---- Load -------------------------------------------------------------------------------------

    /// <summary>
    /// The shared load. Public entry points call this: the three smaller pages pass their id straight
    /// through, MediaDetails stashes its list entry first.
    /// </summary>
    protected async Task LoadCoreAsync(int id)
    {
        LastRequestedId = id;

        if (id <= 0)
        {
            Logger.LogInformation("NAVTRACE {TracePrefix} load aborted — invalid {EntityNoun} id {Id}", TracePrefix, EntityNoun, id);
            Entity = null;
            LoadedId = 0;
            var (invalidTitle, invalidSubtitle) = InvalidIdError;
            ShowError(invalidTitle, invalidSubtitle, canRetry: false);
            return;
        }

        // Same entity already loaded: keep its sections + sort and just restore Content state. This is hit
        // when returning from a pushed sub-page and — importantly — when a CommunityToolkit sort popup
        // closes (it fires the host page's OnNavigatedTo → reload). Without this guard the popup would reset
        // the sort the user just picked.
        if (Entity is not null && Entity.Id == id)
        {
            // This is the #127 path for all four details pages. They are registered transient but
            // stay alive in their tab's navigation stack, so tabbing to Settings, changing a display
            // setting and tabbing back lands here — on the same instance, holding sections built
            // under the old setting, with no fetch to rebuild them.
            ReprojectIfDisplaySettingsChanged();
            OnEntityReused();
            CurrentState = PageState.Content;
            return;
        }

        LoadedId = id;
        var generation = ++_loadGeneration;
        var token = Scope.Begin(); // fresh page scope; OnNavigatedFrom cancels it on navigate-away

        IsBusy = true;

        // Drop the previous entity before fetching. Leaving it in place lets a failed load strand a stale
        // entity whose sections have already been reset, so navigating back to it would hit the same-id
        // guard above and show Content with empty sections.
        Entity = null;
        CurrentState = PageState.InitialLoading;
        ResetForNewEntity();

        var stopwatch = Stopwatch.StartNew();
        Logger.LogInformation("NAVTRACE {TracePrefix} load start ({EntityNoun} {Id})", TracePrefix, EntityNoun, id);
        OnLoadStarting(id);

        try
        {
            IsAuthenticated = !string.IsNullOrWhiteSpace(await _authService.GetAccessTokenAsync(token).ConfigureAwait(true));
            ToggleFavouriteCommand.NotifyCanExecuteChanged();
            OnAuthenticationResolved();

            var entity = await FetchAsync(id, token).ConfigureAwait(true);
            if (entity is null)
            {
                Logger.LogInformation(
                    "NAVTRACE {TracePrefix} not found in {ElapsedMs}ms ({EntityNoun} {Id})",
                    TracePrefix, stopwatch.ElapsedMilliseconds, EntityNoun, id);
                var (notFoundTitle, notFoundSubtitle) = NotFoundError;
                ShowError(notFoundTitle, notFoundSubtitle, canRetry: NullResultIsRetryable);
                return;
            }

            Entity = entity;
            SeedSections(entity);

            // Seeded from the settings as they stand now, so the next appearance has nothing to
            // re-project.
            MarkDisplaySettingsRendered();

            CurrentState = PageState.Content;
            Logger.LogInformation(
                "NAVTRACE {TracePrefix} fetch+seed in {ElapsedMs}ms ({EntityNoun} {Id}, {Sections}); UI render follows",
                TracePrefix, stopwatch.ElapsedMilliseconds, EntityNoun, id, DescribeSeededSections());
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation(
                "NAVTRACE {TracePrefix} load cancelled after {ElapsedMs}ms ({EntityNoun} {Id})",
                TracePrefix, stopwatch.ElapsedMilliseconds, EntityNoun, id);
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;
            var isNotFound = apiEx?.Kind == ApiErrorKind.NotFound;
            if (isNotFound)
            {
                // NotFound is non-retryable and intentionally kept out of Sentry — log at Warning so it stays a breadcrumb.
                Logger.LogWarning(
                    ex,
                    "NAVTRACE {TracePrefix} not found on AniList in {ElapsedMs}ms ({EntityNoun} {Id})",
                    TracePrefix, stopwatch.ElapsedMilliseconds, EntityNoun, id);
            }
            else
            {
                Logger.LogError(
                    ex,
                    "NAVTRACE {TracePrefix} load failed in {ElapsedMs}ms ({EntityNoun} {Id})",
                    TracePrefix, stopwatch.ElapsedMilliseconds, EntityNoun, id);
            }

            var (title, subtitle) = DescribeError(ex);
            ShowError(
                title,
                subtitle,
                canRetry: !isNotFound,
                details: isNotFound ? string.Empty : _errorReportService.Record(ex, $"Load {EntityNoun} details"),
                iconGlyph: apiEx?.IconGlyph);
        }
        finally
        {
            // Only the live load clears this. A superseded load reaches here after its token is
            // cancelled, and on device its continuation is posted rather than run inline — so without
            // the guard it would clear IsBusy while its replacement is still fetching.
            if (generation == _loadGeneration)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>Aborts in-flight work — call from the page's <c>OnNavigatedFrom</c> (#132).</summary>
    public void CancelInFlight() => Scope.Cancel();

    protected virtual (string Title, string Subtitle) DescribeError(Exception ex)
        => ex is AniListApiException apiEx
            ? (apiEx.UserTitle, apiEx.UserSubtitle)
            : FallbackLoadError;

    /// <summary>
    /// Populates the error state. Public because the details pages' <c>DeferredContentLoader</c>
    /// render-failure callback reports through it too.
    /// </summary>
    /// <param name="iconGlyph">Lets the catch path surface a classified
    /// <see cref="AniListApiException.IconGlyph"/> (e.g. NotFound → DismissCircle24); the static
    /// invalid-id / couldn't-find callers fall back to ErrorCircle24.</param>
    public void ShowError(string title, string subtitle, bool canRetry, string details = "", string? iconGlyph = null)
    {
        ErrorTitle = title;
        ErrorSubtitle = subtitle;
        ErrorIconGlyph = iconGlyph ?? Glyphs.Regular.ErrorCircle24;
        ErrorDetails = details;
        CanRetry = canRetry;
        CurrentState = PageState.Error;
    }

    // ---- Commands ---------------------------------------------------------------------------------

    [RelayCommand]
    private Task RetryLoad() => RetryLoadCore();

    protected virtual Task RetryLoadCore() => LoadCoreAsync(LastRequestedId);

    [RelayCommand]
    private async Task OpenSiteUrl()
    {
        var url = SiteUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        // IExternalBrowser swallows and logs its own failures.
        await _browser.OpenAsync(new Uri(url));
    }

    [RelayCommand(CanExecute = nameof(CanToggleFavourite))]
    private async Task ToggleFavourite()
    {
        var entity = Entity;
        // Re-check the gate here (not just via the command's CanExecute) so the failure-snackbar
        // Retry can't run an optimistic flip if auth/busy state changed since the failure.
        if (entity is null || !CanToggleFavourite)
        {
            return;
        }

        if (await _favouriteRunner.ToggleAsync(entity, FavouriteKind, NotifyFavouriteChanged, () => _ = ToggleFavourite()))
        {
            SentrySdk.AddBreadcrumb($"Favourite toggled ({EntityNoun} {entity.Id} → {(entity.IsFavourite ? "on" : "off")})", "list", "user");
        }
    }

    private void NotifyFavouriteChanged()
    {
        OnPropertyChanged(nameof(IsFavourite));
        OnPropertyChanged(nameof(FavouritesDisplay));
        OnPropertyChanged(nameof(HasFavourites));
        ToggleFavouriteCommand.NotifyCanExecuteChanged();
        OnFavouriteChanged();
    }

    [RelayCommand]
    private async Task NavigateToMedia(RelatedMedia? media)
    {
        var mediaId = media?.Id ?? 0;
        Logger.LogInformation("NAVTRACE {TracePrefix} → Media with id={MediaId}", TracePrefix, mediaId);
        if (mediaId <= 0)
        {
            return;
        }

        // The details screen is anime-only (Media(id:, type: ANIME)); a manga/novel id would 404, and the
        // media lists these pages show can include manga. Toast instead of navigating.
        if (media is { IsAnime: false })
        {
            Logger.LogInformation("NAVTRACE {TracePrefix} → Media skipped non-anime {MediaId} (type={Type}).", TracePrefix, mediaId, media.Type);
            await Feedback.ShowToastAsync(BioLinkFollower.UnsupportedMediaMessage);
            return;
        }

        await NavigationService.GoToAsync("media-details", animate: false, new Dictionary<string, object>
        {
            ["mediaId"] = mediaId,
        });
    }
}
