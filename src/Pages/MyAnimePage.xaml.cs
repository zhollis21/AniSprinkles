using Microsoft.Extensions.DependencyInjection;

namespace AniSprinkles.Pages
{
    public partial class MyAnimePage : ContentPage
    {
        private readonly MyAnimePageModel _viewModel;

        public MyAnimePage()
            : this(ResolveViewModel())
        {
        }

        public MyAnimePage(MyAnimePageModel viewModel)
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

        private static MyAnimePageModel ResolveViewModel()
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null)
                throw new InvalidOperationException("Service provider not available.");

            return services.GetRequiredService<MyAnimePageModel>();
        }
    }
}
