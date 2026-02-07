using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniSprinkles.PageModels
{
    public partial class MyAnimePageModel : ObservableObject
    {
        private readonly IAniListClient _aniListClient;
        private readonly IAuthService _authService;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isAuthenticated;

        [ObservableProperty]
        private bool _isNotAuthenticated = true;

        [ObservableProperty]
        private string _title = "My Anime";

        [ObservableProperty]
        private ObservableCollection<MediaListEntry> _entries = [];

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _hasStatusMessage;

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
                var token = await _authService.GetAccessTokenAsync();
                IsAuthenticated = !string.IsNullOrWhiteSpace(token);

                if (!IsAuthenticated)
                {
                    Title = "Sign in required";
                    StatusMessage = "Sign in to see your AniList.";
                    Entries = [];
                    return;
                }

                Title = "My Anime";
                StatusMessage = string.Empty;
                var list = await _aniListClient.GetMyAnimeListAsync();
                Entries = new ObservableCollection<MediaListEntry>(list);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                Entries = [];
            }
            finally
            {
                IsBusy = false;
            }
        }

        partial void OnStatusMessageChanged(string value)
            => HasStatusMessage = !string.IsNullOrWhiteSpace(value);

        partial void OnIsAuthenticatedChanged(bool value)
            => IsNotAuthenticated = !value;

        [RelayCommand]
        private async Task SignIn()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                var signedIn = await _authService.SignInAsync();
                if (!signedIn)
                {
                    StatusMessage = "Sign in canceled.";
                    return;
                }
            }
            finally
            {
                IsBusy = false;
            }

            await LoadAsync();
        }

        [RelayCommand]
        private async Task SignOut()
        {
            await _authService.SignOutAsync();
            await LoadAsync();
        }
    }
}
