using AniSprinkles.PageModels;
using Microsoft.Extensions.DependencyInjection;

namespace AniSprinkles.Pages
{
    public partial class SettingsPage : ContentPage
    {
        private readonly SettingsPageModel _viewModel;

        public SettingsPage()
            : this(ResolveViewModel())
        {
        }

        public SettingsPage(SettingsPageModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.LoadAsync();
        }

        private static SettingsPageModel ResolveViewModel()
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null)
                throw new InvalidOperationException("Service provider not available.");

            return services.GetRequiredService<SettingsPageModel>();
        }
    }
}
