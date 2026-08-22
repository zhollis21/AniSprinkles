using System.ComponentModel;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Pages;

public partial class CharacterDetailsPage : ContentPage, IQueryAttributable
{
    private CharacterDetailsPageModel ViewModel { get; }
    private ILogger<CharacterDetailsPage> Logger { get; }
    private readonly DeferredContentLoader _loader;
    private int _pendingCharacterId;

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

        _loader = new DeferredContentLoader(
            logger,
            LoadedContentHost,
            entityName: "character",
            shouldShowContent: () => ViewModel.HasCharacter && !ViewModel.IsBusy && ViewModel.CurrentState == PageState.Content,
            createView: () => new Views.CharacterDetailsLoadedContentView { BindingContext = ViewModel },
            onRenderError: ex =>
            {
                ViewModel.ErrorTitle = "Something Went Wrong";
                ViewModel.ErrorSubtitle = "Failed to render the character view.";
                ViewModel.ErrorIconGlyph = FluentIconsRegular.ErrorCircle24;
                ViewModel.ErrorDetails = $"{ex.GetType().Name}: {ex.Message}";
                ViewModel.CanRetry = true;
                ViewModel.CurrentState = PageState.Error;
            });

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
        var characterId = QueryAttributeParser.ParseInt(query, "characterId");
        Logger.LogInformation("NAVTRACE CharacterDetailsPage.ApplyQueryAttributes characterId={CharacterId}", characterId);

        _loader.ResetContentIfStale(characterId != _pendingCharacterId);
        _pendingCharacterId = characterId;
        _loader.BumpVersion();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingCharacterId));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loader.OnAppearing();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingCharacterId));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _loader.OnDisappearing();
        // Abandon any in-flight fetches so a half-loaded page doesn't keep hitting the API after
        // the user has navigated away.
        ViewModel.CancelInFlight();
    }

    private async Task RunDeferredLoadAsync(int version, int characterId)
    {
        try
        {
            await Task.Yield();

            if (!_loader.IsCurrent(version))
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
            _loader.UpdateHost();
        }
    }
}
