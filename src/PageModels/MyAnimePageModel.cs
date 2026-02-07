using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace AniSprinkles.PageModels
{
    public partial class MyAnimePageModel : ObservableObject
    {
        private readonly IAniListClient _aniListClient;
        private readonly IAuthService _authService;
        private readonly ErrorReportService _errorReportService;
        private readonly ILogger<MyAnimePageModel> _logger;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isAuthenticated;

        [ObservableProperty]
        private string _title = "My Anime";

        [ObservableProperty]
        private ObservableCollection<MediaListSection> _sections = [];

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _hasStatusMessage;

        [ObservableProperty]
        private string _errorDetails = string.Empty;

        [ObservableProperty]
        private bool _hasErrorDetails;

        [ObservableProperty]
        private bool _isErrorDetailsVisible;

        public MyAnimePageModel(IAniListClient aniListClient, IAuthService authService, ErrorReportService errorReportService, ILogger<MyAnimePageModel> logger)
        {
            _aniListClient = aniListClient;
            _authService = authService;
            _errorReportService = errorReportService;
            _logger = logger;
        }

        public async Task LoadAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                _logger.LogInformation("Loading My Anime list.");
                Sentry.SentrySdk.AddBreadcrumb("Load My Anime list", "navigation", "state");
                var token = await _authService.GetAccessTokenAsync();
                IsAuthenticated = !string.IsNullOrWhiteSpace(token);

                if (!IsAuthenticated)
                {
                    Title = "Sign in required";
                    StatusMessage = "Sign in to see your AniList.";
                    ErrorDetails = string.Empty;
                    IsErrorDetailsVisible = false;
                    Sections = [];
                    return;
                }

                Title = "My Anime";
                StatusMessage = string.Empty;
                ErrorDetails = string.Empty;
                IsErrorDetailsVisible = false;
                Sentry.SentrySdk.AddBreadcrumb("Fetching AniList list", "http", "state");
                var list = await _aniListClient.GetMyAnimeListAsync();
                Sections = BuildSections(list);
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to load list. Tap Details for more.";
                ErrorDetails = _errorReportService.Record(ex, "Load My Anime");
                IsErrorDetailsVisible = false;
                Sections = [];
            }
            finally
            {
                IsBusy = false;
            }
        }

        partial void OnStatusMessageChanged(string value)
            => HasStatusMessage = !string.IsNullOrWhiteSpace(value);

        partial void OnErrorDetailsChanged(string value)
            => HasErrorDetails = !string.IsNullOrWhiteSpace(value);


        [RelayCommand]
        private async Task SignIn()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                _logger.LogInformation("Sign-in requested.");
                Sentry.SentrySdk.AddBreadcrumb("Sign-in requested", "auth", "user");
                var signedIn = await _authService.SignInAsync();
                if (!signedIn)
                {
                    StatusMessage = "Sign in canceled.";
                    Sentry.SentrySdk.AddBreadcrumb("Sign-in canceled", "auth", "user");
                    return;
                }
                Sentry.SentrySdk.AddBreadcrumb("Sign-in successful", "auth", "user");
            }
            catch (Exception ex)
            {
                StatusMessage = "Sign in failed. Tap Details for more.";
                ErrorDetails = _errorReportService.Record(ex, "Sign in");
                IsErrorDetailsVisible = false;
                return;
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
            _logger.LogInformation("Sign-out requested.");
            Sentry.SentrySdk.AddBreadcrumb("Sign-out requested", "auth", "user");
            await _authService.SignOutAsync();
            await LoadAsync();
        }

        [RelayCommand]
        private async Task OpenDetails(MediaListEntry? entry)
        {
            if (entry is null)
                return;

            var mediaId = entry.MediaId != 0 ? entry.MediaId : entry.Media?.Id ?? 0;
            if (mediaId <= 0)
            {
                StatusMessage = "Unable to open details.";
                return;
            }

            Sentry.SentrySdk.AddBreadcrumb($"Open details {mediaId}", "navigation", "state");
            await Shell.Current.GoToAsync("media-details", new Dictionary<string, object>
            {
                ["mediaId"] = mediaId,
                ["listEntry"] = entry
            });
        }

        private static ObservableCollection<MediaListSection> BuildSections(IReadOnlyList<MediaListEntry> entries)
        {
            var sections = new List<MediaListSection>
            {
                new("Watching", true),
                new("Planning", true),
                new("Completed", false),
                new("Paused", false),
                new("Dropped", false),
                new("Repeating", false)
            };

            var map = new Dictionary<MediaListStatus, MediaListSection>
            {
                [MediaListStatus.Current] = sections[0],
                [MediaListStatus.Planning] = sections[1],
                [MediaListStatus.Completed] = sections[2],
                [MediaListStatus.Paused] = sections[3],
                [MediaListStatus.Dropped] = sections[4],
                [MediaListStatus.Repeating] = sections[5]
            };

            var unknown = new MediaListSection("Other", false);
            var buckets = sections.ToDictionary(section => section, _ => new List<MediaListEntry>());
            var unknownBucket = new List<MediaListEntry>();

            foreach (var entry in entries)
            {
                if (entry.Status is null || !map.TryGetValue(entry.Status.Value, out var section))
                {
                    unknownBucket.Add(entry);
                    continue;
                }

                buckets[section].Add(entry);
            }

            foreach (var section in sections)
            {
                section.AddItems(buckets[section]);
            }

            if (unknownBucket.Count > 0)
            {
                unknown.AddItems(unknownBucket);
            }

            var result = new ObservableCollection<MediaListSection>(
                sections.Where(s => s.TotalCount > 0));

            if (unknown.TotalCount > 0)
            {
                result.Add(unknown);
            }

            return result;
        }

        [RelayCommand]
        private void ToggleDetails()
        {
            if (!HasErrorDetails)
                return;

            IsErrorDetailsVisible = !IsErrorDetailsVisible;
        }

        [RelayCommand]
        private async Task CopyError()
        {
            if (!HasErrorDetails)
                return;

            await Clipboard.Default.SetTextAsync(ErrorDetails);
            StatusMessage = "Error details copied.";
        }

        [RelayCommand]
        private async Task ShareError()
        {
            if (!HasErrorDetails)
                return;

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Text = ErrorDetails,
                Title = "AniSprinkles Error Details"
            });
        }
    }
}
