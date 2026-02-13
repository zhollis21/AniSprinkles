using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

public partial class SettingsPageModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAniListClient _aniListClient;
    private readonly ILogger<SettingsPageModel> _logger;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private string _aniListUserId = string.Empty;

    public SettingsPageModel(IAuthService authService, IAniListClient aniListClient, ILogger<SettingsPageModel> logger)
    {
        _authService = authService;
        _aniListClient = aniListClient;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        await RefreshAuthStateAsync();
        StatusMessage = IsAuthenticated ? "Signed in to AniList." : "Not signed in.";

        // If authenticated, fetch the user ID from AniList
        if (IsAuthenticated)
        {
            try
            {
                var userId = await _aniListClient.GetCurrentUserIdAsync();
                AniListUserId = userId.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch AniList user ID");
                AniListUserId = string.Empty;
            }
        }
        else
        {
            AniListUserId = string.Empty;
        }

        SentrySdk.AddBreadcrumb("Settings loaded", "navigation", "state");
    }


    partial void OnStatusMessageChanged(string value)
        => HasStatusMessage = !string.IsNullOrWhiteSpace(value);

    [RelayCommand]
    private async Task SignIn()
    {
        _logger.LogInformation("Sign-in requested from Settings.");
        try
        {
            SentrySdk.AddBreadcrumb("Sign-in requested (Settings)", "auth", "user");
            var signedIn = await _authService.SignInAsync();
            await RefreshAuthStateAsync();

            if (signedIn)
            {
                // Fetch the user ID after successful sign-in
                try
                {
                    var userId = await _aniListClient.GetCurrentUserIdAsync();
                    AniListUserId = userId.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch user ID after sign-in");
                }
                StatusMessage = "Signed in to AniList.";
            }
            else
            {
                StatusMessage = "Sign in canceled.";
            }

            SentrySdk.AddBreadcrumb(
                signedIn ? "Sign-in successful (Settings)" : "Sign-in canceled (Settings)",
                "auth",
                "user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sign-in failed.");
            SentrySdk.AddBreadcrumb("Sign-in failed (Settings)", "auth", "user");
            StatusMessage = "Sign in failed. Try again.";
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        _logger.LogInformation("Sign-out requested from Settings.");
        SentrySdk.AddBreadcrumb("Sign-out requested (Settings)", "auth", "user");
        await _authService.SignOutAsync();
        await RefreshAuthStateAsync();
        AniListUserId = string.Empty;
        StatusMessage = "Signed out.";
    }

    private async Task RefreshAuthStateAsync()
    {
        var token = await _authService.GetAccessTokenAsync();
        IsAuthenticated = !string.IsNullOrWhiteSpace(token);
    }
}
