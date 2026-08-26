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
            onRenderError: ex => ViewModel.ShowError(
                "Something Went Wrong",
                "Failed to render the character view.",
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
        var characterId = QueryAttributeParser.ParseInt(query, "characterId");
        Logger.LogInformation("NAVTRACE CharacterDetailsPage.ApplyQueryAttributes characterId={CharacterId}", characterId);

        _loader.ResetContentIfStale(characterId != _pendingCharacterId);
        _pendingCharacterId = characterId;
        _loader.BumpVersion();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingCharacterId));
    }


    // This page's whole appear/disappear lifecycle hangs off the navigation hooks rather than
    // OnAppearing/OnDisappearing: on a pushed page a tab switch fires only these two (#127, #132 —
    // see DetailsPageModelBase.RefreshDisplaySettings for the measured hook table). They must stay
    // paired; the re-arm here is what stops _loader latching off after one tab round-trip.
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        ViewModel.RefreshDisplaySettings();
        _loader.OnAppearing();
        _loader.TrySchedule(version => RunDeferredLoadAsync(version, _pendingCharacterId));
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _loader.OnDisappearing();
        // Abandon any in-flight fetches so a half-loaded page doesn't keep hitting the API after
        // the user has navigated away. List ops recreate the scope via EnsureActive, so the sort
        // popup — which fires this too — stays harmless.
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
