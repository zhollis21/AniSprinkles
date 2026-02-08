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

        private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaListEntry entry)
                return;

            await _viewModel.OpenDetailsCommand.ExecuteAsync(entry);

            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
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
