using System.ComponentModel;
using Microsoft.Extensions.Logging;
using AniSprinkles.Utilities;

namespace AniSprinkles.Pages;

public partial class CharacterDetailsPage : ContentPage, IQueryAttributable
{
    private CharacterDetailsPageModel ViewModel { get; }
    private ILogger<CharacterDetailsPage> Logger { get; }
    private bool _hasCreatedLoadedContent;
    private bool _hasAppeared;
    private int _pendingCharacterId;
    private int _pendingQueryVersion;
    private int _scheduledQueryVersion;

    public CharacterDetailsPage()
        : this(
            ServiceProviderHelper.GetServiceProvider().GetRequiredService<CharacterDetailsPageModel>(),
            ServiceProviderHelper.GetServiceProvider().GetRequiredService<ILogger<CharacterDetailsPage>>())
    {
    }

    public CharacterDetailsPage(CharacterDetailsPageModel viewModel, ILogger<CharacterDetailsPage> logger)
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
        var characterId = 0;
        if (query.TryGetValue("characterId", out var raw))
        {
            if (raw is int id)
            {
                characterId = id;
            }
            else if (raw is string text && int.TryParse(text, out var parsed))
            {
                characterId = parsed;
            }
        }

        Logger.LogInformation("NAVTRACE CharacterDetailsPage.ApplyQueryAttributes characterId={CharacterId}", characterId);

        if (characterId != _pendingCharacterId || !_hasCreatedLoadedContent)
        {
            HandlerHelper.DisconnectAll(LoadedContentHost.Content);
            LoadedContentHost.Content = null;
            _hasCreatedLoadedContent = false;
        }

        _pendingCharacterId = characterId;
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
        var characterId = _pendingCharacterId;
        _scheduledQueryVersion = version;

        RunDeferredLoadAsync(version, characterId)
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        Logger.LogError(task.Exception, "CharacterDetailsPage deferred load faulted for character {CharacterId}", characterId);
                    }
                },
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task RunDeferredLoadAsync(int version, int characterId)
    {
        try
        {
            await Task.Yield();

            if (!_hasAppeared || version != _pendingQueryVersion)
            {
                return;
            }

            await ViewModel.LoadAsync(characterId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "CharacterDetailsPage load failed for character {CharacterId}", characterId);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CharacterDetailsPageModel.IsBusy)
            or nameof(CharacterDetailsPageModel.HasCharacter)
            or nameof(CharacterDetailsPageModel.CurrentState))
        {
            UpdateLoadedContentHost();
        }
    }

    private void UpdateLoadedContentHost()
    {
        if (ViewModel.HasCharacter && !ViewModel.IsBusy && ViewModel.CurrentState == PageState.Content)
        {
            if (!_hasCreatedLoadedContent)
            {
                try
                {
                    LoadedContentHost.Content = new Views.CharacterDetailsLoadedContentView
                    {
                        BindingContext = ViewModel
                    };
                    _hasCreatedLoadedContent = true;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to create CharacterDetailsLoadedContentView");
                    ViewModel.ErrorTitle = "Something Went Wrong";
                    ViewModel.ErrorSubtitle = "Failed to render the character view.";
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
