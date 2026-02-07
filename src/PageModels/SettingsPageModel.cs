using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels
{
    public partial class SettingsPageModel : ObservableObject
    {
        private readonly IAuthService _authService;
        private readonly ILogger<SettingsPageModel> _logger;

        [ObservableProperty]
        private bool _isAuthenticated;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _hasStatusMessage;

        public SettingsPageModel(IAuthService authService, ILogger<SettingsPageModel> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        public async Task LoadAsync()
        {
            await RefreshAuthStateAsync();
            StatusMessage = IsAuthenticated ? "Signed in to AniList." : "Not signed in.";
            Sentry.SentrySdk.AddBreadcrumb("Settings loaded", "navigation", "state");
        }


        partial void OnStatusMessageChanged(string value)
            => HasStatusMessage = !string.IsNullOrWhiteSpace(value);

        [RelayCommand]
        private async Task SignIn()
        {
            _logger.LogInformation("Sign-in requested from Settings.");
            try
            {
                Sentry.SentrySdk.AddBreadcrumb("Sign-in requested (Settings)", "auth", "user");
                var signedIn = await _authService.SignInAsync();
                await RefreshAuthStateAsync();
                StatusMessage = signedIn ? "Signed in to AniList." : "Sign in canceled.";
                Sentry.SentrySdk.AddBreadcrumb(
                    signedIn ? "Sign-in successful (Settings)" : "Sign-in canceled (Settings)",
                    "auth",
                    "user");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sign-in failed.");
                Sentry.SentrySdk.AddBreadcrumb(
                    message: "Sign-in failed (Settings)",
                    category: "auth",
                    type: "user",
                    level: Sentry.BreadcrumbLevel.Error);
                StatusMessage = "Sign in failed. Try again.";
            }
        }

        [RelayCommand]
        private async Task SignOut()
        {
            _logger.LogInformation("Sign-out requested from Settings.");
            Sentry.SentrySdk.AddBreadcrumb("Sign-out requested (Settings)", "auth", "user");
            await _authService.SignOutAsync();
            await RefreshAuthStateAsync();
            StatusMessage = "Signed out.";
        }

        private async Task RefreshAuthStateAsync()
        {
            var token = await _authService.GetAccessTokenAsync();
            IsAuthenticated = !string.IsNullOrWhiteSpace(token);
        }
    }
}
