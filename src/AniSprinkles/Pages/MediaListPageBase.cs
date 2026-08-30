using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

/// <summary>
/// The lifecycle both halves of the Library tab share (#12): deferred content creation so the
/// Shell transition finishes before the heavy view is built, toolbar items that come and go with
/// auth, the sort-popup anchor, and the handler teardown that stops a singleton page model pinning
/// an orphaned page.
/// <para>
/// A base class rather than two copies because this is the code that has needed the careful fixes —
/// #60’s colour flash, #64’s measure strategy, the OnNavigatedTo-vs-OnAppearing lesson in
/// AGENTS.md — and it sits on the MAUI side of the test boundary, where a divergence between two
/// copies would have nothing watching it.
/// </para>
/// </summary>
public abstract partial class MediaListPageBase : ContentPage
{
    private static readonly TimeSpan DeferredLoadDelay = TimeSpan.FromMilliseconds(120);

    private MediaListPageModel? _viewModel;

    /// <summary>Resolves this half’s page model. The only thing the subclasses supply.</summary>
    protected abstract MediaListPageModel? ResolveViewModel(IServiceProvider services);
    private bool _hasAppeared;
    private bool _hasCreatedLoadedContent;
    private int _loadVersion;
    private ToolbarItem? _sortToolbarItem;
    private ToolbarItem? _searchToolbarItem;
    private ToolbarItem? _viewModeToolbarItem;
    private ContentView? _loadedContentHost;
    private FontImageSource? _sortIcon;
    private FontImageSource? _viewModeIcon;
    private readonly ILogger<MediaListPageBase>? _logger;

    // Re-entrancy guard: a second tap while the picker is up would stack popups (mirrors SortDropdown).
    private bool _sortPopupOpen;

    /// <summary>
    /// Hands the base the elements XAML generated into the SUBCLASS partial. They cannot be reached
    /// from here directly — the generated fields belong to whichever concrete page compiled the
    /// .xaml — so each page calls this straight after InitializeComponent. Explicit rather than
    /// FindByName so a renamed x:Name breaks the build instead of quietly nulling a field on a page
    /// whose lifecycle has already needed careful fixes.
    /// </summary>
    protected void AttachXamlElements(
        ContentView loadedContentHost,
        ToolbarItem sortToolbarItem,
        ToolbarItem searchToolbarItem,
        ToolbarItem viewModeToolbarItem,
        FontImageSource sortIcon,
        FontImageSource viewModeIcon)
    {
        _loadedContentHost = loadedContentHost;
        // Stashed so we can add/remove them based on auth state.
        _sortToolbarItem = sortToolbarItem;
        _searchToolbarItem = searchToolbarItem;
        _viewModeToolbarItem = viewModeToolbarItem;
        _sortIcon = sortIcon;
        _viewModeIcon = viewModeIcon;
    }

    protected MediaListPageBase()
    {
        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<MediaListPageBase>();
        }
        catch (InvalidOperationException)
        {
        }
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

        // The view mode is shared with media-browse (View All); re-read it in case it was
        // changed there since this singleton VM was constructed.
        _viewModel.SyncViewModeFromPreference();
        UpdateViewModeIcon(_viewModel.CurrentViewMode);
        UpdateSortIcon();
        UpdateToolbarItems();

        // Content survived the tab switch — just refresh data in background.
        if (_loadedContentHost?.Content is not null)
        {
            await _viewModel.LoadAsync();
            // Tear down loaded content if the user signed out while away.
            UpdateLoadedContentHost();
            return;
        }

        // Content needs to be (re)created.
        _hasCreatedLoadedContent = false;

        int version;

        // Fast path: the singleton ViewModel already has cached sections from a
        // previous visit. We still defer view creation so the Shell transition
        // animation completes first (InitializeComponent of the heavy content
        // view blocks the UI thread), but we skip the API call. Flip CurrentState
        // to InitialLoading during the delay so the spinner is visible instead of
        // a blank page.
        if (_viewModel.HasLoadedData)
        {
            var savedState = _viewModel.CurrentState;
            _viewModel.CurrentState = PageState.InitialLoading;
            version = ++_loadVersion;
            await Task.Yield();
            await Task.Delay(DeferredLoadDelay);

            if (!_hasAppeared || version != _loadVersion)
            {
                // Abort: only restore state if we're still the one showing the spinner.
                if (_viewModel.CurrentState == PageState.InitialLoading)
                {
                    _viewModel.CurrentState = savedState;
                }
                return;
            }

            _viewModel.CurrentState = PageState.Content;
            UpdateLoadedContentHost();
            // Background refresh with existing data visible.
            await _viewModel.LoadAsync();
            return;
        }

        // Slow path (first load): yield so the Shell transition animation can
        // complete before we run the data fetch and create the heavy XAML
        // content view. The XAML-bound loading overlay will be visible.
        version = ++_loadVersion;
        await Task.Yield();
        await Task.Delay(DeferredLoadDelay);

        if (!_hasAppeared || version != _loadVersion)
        {
            return;
        }

        await _viewModel.LoadAsync();
        UpdateLoadedContentHost();
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

        // Transient page + singleton page model: when the platform view is torn down (Handler
        // becomes null) drop the PropertyChanged subscription, or the singleton would pin this
        // orphaned page and stack a duplicate handler if Shell recreates/reattaches the page.
        if (Handler is null)
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            return;
        }

        EnsureViewModel();

        // Re-subscribe on reattach (EnsureViewModel no-ops once the VM is set); -= keeps it idempotent.
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UpdateLoadedContentHost()
    {
        // Null only if a subclass forgot AttachXamlElements; bail rather than suppress, so a page
        // that skipped it renders empty instead of throwing on a lifecycle callback.
        if (_loadedContentHost is not ContentView host)
        {
            return;
        }

        var isError = _viewModel?.CurrentState == PageState.Error;
        var isAuth = _viewModel?.IsAuthenticated == true;

        if (isAuth && !isError && !_hasCreatedLoadedContent)
        {
            var view = new Views.MediaListLoadedContentView
            {
                BindingContext = _viewModel
            };

            _logger?.LogInformation(
                "LOADEDHOST MediaList attach (isAuth={IsAuth}, isError={IsError}, currentState={CurrentState})",
                isAuth, isError, _viewModel?.CurrentState);
            host.Content = view;
            _hasCreatedLoadedContent = true;
        }
        else if ((!isAuth || isError) && _hasCreatedLoadedContent)
        {
            _logger?.LogInformation(
                "LOADEDHOST MediaList detach (isAuth={IsAuth}, isError={IsError}, currentState={CurrentState})",
                isAuth, isError, _viewModel?.CurrentState);
            HandlerHelper.DisconnectAll(host.Content);
            host.Content = null;
            _hasCreatedLoadedContent = false;
        }
    }

    private void EnsureViewModel()
    {
        if (_viewModel is not null)
        {
            return;
        }

        try
        {
            var services = ServiceProviderHelper.GetServiceProvider();
            var viewModel = services is null ? null : ResolveViewModel(services);
            if (viewModel is null)
            {
                return;
            }

            SetViewModel(viewModel);
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Create the loaded content view only when CurrentState == Content. Gating
        // on Content (not just "not Error") keeps the heavy XAML InitializeComponent
        // off the UI thread while CurrentState == InitialLoading — OnAppearing flips
        // to InitialLoading during the defer delay, and we don't want the view
        // materialized until the Shell transition animation has finished.
        if ((e.PropertyName is nameof(MediaListPageModel.IsAuthenticated)
                or nameof(MediaListPageModel.Sections)
                or nameof(MediaListPageModel.CurrentState))
            && _hasAppeared
            && _viewModel?.IsAuthenticated == true
            && _viewModel.CurrentState == PageState.Content
            && _viewModel.Sections.Count > 0)
        {
            UpdateLoadedContentHost();
            UpdateToolbarItems();
        }
        else if (e.PropertyName == nameof(MediaListPageModel.CurrentState)
            && _hasAppeared
            && _viewModel?.CurrentState == PageState.Error)
        {
            // Tear down loaded content so the error view is visible.
            UpdateLoadedContentHost();
        }
        else if (e.PropertyName == nameof(MediaListPageModel.IsAuthenticated)
            && _hasAppeared
            && _viewModel?.IsAuthenticated != true)
        {
            // Tear down loaded content when the user signs out.
            UpdateLoadedContentHost();
            UpdateToolbarItems();
        }
        else if (e.PropertyName == nameof(MediaListPageModel.ViewModeIconGlyph) && _viewModel is not null)
        {
            UpdateViewModeIcon(_viewModel.CurrentViewMode);
        }
        else if (e.PropertyName == nameof(MediaListPageModel.SortIconGlyph) && _viewModel is not null)
        {
            UpdateSortIcon();
        }
    }

    private void UpdateToolbarItems()
    {
        if (_sortToolbarItem is null || _searchToolbarItem is null || _viewModeToolbarItem is null)
        {
            return;
        }

        bool authenticated = _viewModel?.IsAuthenticated == true;
        bool hasSearch = ToolbarItems.Contains(_searchToolbarItem);

        if (authenticated && !hasSearch)
        {
            ToolbarItems.Add(_sortToolbarItem);
            ToolbarItems.Add(_searchToolbarItem);
            ToolbarItems.Add(_viewModeToolbarItem);
        }
        else if (!authenticated && hasSearch)
        {
            ToolbarItems.Remove(_sortToolbarItem);
            ToolbarItems.Remove(_searchToolbarItem);
            ToolbarItems.Remove(_viewModeToolbarItem);
        }
    }

    // Opens the shared sort picker (SortPopup) anchored just below the top bar, right-aligned. Unlike the
    // carousel SortDropdown (a pill that measures itself), there's no anchor view in the toolbar, so we
    // compute a fixed top-right "open-down" anchor from the display metrics. The picked code is handed to
    // the page model's SelectSort command, which applies + persists the sort.
    private async void OnSortClicked(object? sender, EventArgs e)
    {
        if (_sortPopupOpen || _viewModel?.SortOptions is not { Count: > 0 } options)
        {
            return;
        }

        _sortPopupOpen = true;
        try
        {
            var info = DeviceDisplay.Current.MainDisplayInfo;
            var density = info.Density > 0 ? info.Density : 1;

            // SortPopup positions the card in popup-PAGE coordinates, which exclude the left/right system-bar
            // insets (matching SortDropdown.ComputeAnchor). Subtract those insets so the right-aligned card
            // lands correctly even with side insets (landscape / multi-window / cutouts); both are 0 on a
            // standard portrait phone, so this is a no-op under the current SensorPortrait lock.
            var (leftInsetPx, rightInsetPx) = GetHorizontalSystemBarInsetsPx();
            var pageWidthDip = (info.Width - leftInsetPx - rightInsetPx) / density;

            // Right-align the card under the right edge, clamped clear of the screen edge (10dip inset).
            var cardLeft = Math.Max(10, pageWidthDip - Views.SortPopup.CardWidth - 10);
            // Anchor to the bottom of the action bar; open down. Resolved from the theme so it tracks the
            // real toolbar height (56dip portrait phone, 64dip tablet, 48dip landscape) instead of a magic
            // number that only holds under today's portrait lock.
            var actionBarBottomDip = GetActionBarHeightDip(density);

            var result = await Views.SortPopup.ShowAsync(
                options, openUp: false, cardLeft, actionBarBottomDip, gapDip: 6);

            if (!string.IsNullOrEmpty(result)
                && _viewModel.SelectSortCommand.CanExecute(result))
            {
                _viewModel.SelectSortCommand.Execute(result);
            }
        }
        finally
        {
            _sortPopupOpen = false;
        }
    }

    // Left/right system-bar insets in physical pixels (side nav bars / display cutouts). 0 on a standard
    // portrait phone; nonzero only in landscape / multi-window, which the SensorPortrait lock disallows
    // today — read anyway so the sort anchor stays correct if that lock is ever lifted.
    private static (double Left, double Right) GetHorizontalSystemBarInsetsPx()
    {
#if ANDROID
        var insets = Platform.CurrentActivity?.Window?.DecorView?.RootWindowInsets?
            .GetInsets(Android.Views.WindowInsets.Type.SystemBars());
        if (insets is not null)
        {
            return (insets.Left, insets.Right);
        }
#endif
        return (0, 0);
    }

    // The action-bar (toolbar) height in dips, resolved from Android's actionBarSize theme attribute so it
    // tracks the device/orientation (56dip portrait phone, 64dip tablet, 48dip landscape). Falls back to the
    // standard 56dip portrait value if the attribute can't be resolved. Note: this is the theme's action-bar
    // size, which MAUI's MaterialToolbar follows in practice but isn't strictly bound to.
    private static double GetActionBarHeightDip(double density)
    {
#if ANDROID
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var value = new Android.Util.TypedValue();
        if (context?.Theme?.ResolveAttribute(Android.Resource.Attribute.ActionBarSize, value, true) == true)
        {
            var px = Android.Util.TypedValue.ComplexToDimensionPixelSize(value.Data, context.Resources?.DisplayMetrics);
            if (px > 0)
            {
                return px / density;
            }
        }
#endif
        return 56;
    }

    private void UpdateViewModeIcon(ListViewMode mode)
    {
        var glyph = mode switch
        {
            ListViewMode.Large => FluentIconsRegular.Grid24,
            ListViewMode.Compact => FluentIconsRegular.TextBulletListSquare24,
            _ => FluentIconsRegular.List24,
        };

        if (_viewModeIcon is not null)
        {
            _viewModeIcon.Glyph = glyph;
        }
    }

    private void UpdateSortIcon()
    {
        if (_viewModel is not null)
        {
            if (_sortIcon is not null)
            {
                _sortIcon.Glyph = _viewModel.SortIconGlyph;
            }
        }
    }

    private void SetViewModel(MediaListPageModel viewModel)
    {
        _viewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BindingContext = viewModel;
    }
}
