namespace AniSprinkles.Pages;

public partial class SettingsPage : ContentPage
{
    private SettingsPageModel ViewModel { get; }

    public SettingsPage()
        : this(ResolveViewModel())
    {
    }

    public SettingsPage(SettingsPageModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = ViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ViewModel.LoadAsync();
    }

    private static SettingsPageModel ResolveViewModel()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services is null)
        {
            throw new InvalidOperationException("Service provider not available.");
        }

        return services.GetRequiredService<SettingsPageModel>();
    }
}
