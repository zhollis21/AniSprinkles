using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.PageModels
{
    public partial class MyAnimePageModel : ObservableObject
    {
        private readonly IAniListClient _aniListClient;
        private readonly IAuthService _authService;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _title = "My Anime";

        [ObservableProperty]
        private ObservableCollection<MediaListEntry> _entries = [];

        public MyAnimePageModel(IAniListClient aniListClient, IAuthService authService)
        {
            _aniListClient = aniListClient;
            _authService = authService;
        }

        public async Task LoadAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                if (!_authService.IsAuthenticated)
                {
                    Title = "Sign in required";
                    Entries = [];
                    return;
                }

                Title = "My Anime";
                var list = await _aniListClient.GetMyAnimeListAsync();
                Entries = new ObservableCollection<MediaListEntry>(list);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
