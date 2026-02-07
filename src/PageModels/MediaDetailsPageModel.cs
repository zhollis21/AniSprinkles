using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Sentry;

namespace AniSprinkles.PageModels
{
    public partial class MediaDetailsPageModel : ObservableObject
    {
        private readonly IAniListClient _aniListClient;
        private readonly ErrorReportService _errorReportService;
        private readonly ILogger<MediaDetailsPageModel> _logger;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private Media? _media;

        [ObservableProperty]
        private MediaListEntry? _listEntry;

        [ObservableProperty]
        private bool _hasListEntry;

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

        public MediaDetailsPageModel(IAniListClient aniListClient, ErrorReportService errorReportService, ILogger<MediaDetailsPageModel> logger)
        {
            _aniListClient = aniListClient;
            _errorReportService = errorReportService;
            _logger = logger;
        }

        public string PageTitle => Media?.DisplayTitle ?? "Details";

        public string? CoverImageUrl =>
            Media?.CoverImage?.ExtraLarge ??
            Media?.CoverImage?.Large ??
            Media?.CoverImage?.Medium;

        public async Task LoadAsync(int mediaId, MediaListEntry? listEntry)
        {
            if (IsBusy)
                return;

            if (mediaId <= 0)
            {
                StatusMessage = "Details unavailable.";
                return;
            }

            IsBusy = true;
            try
            {
                _logger.LogInformation("Loading details for media {MediaId}.", mediaId);
                SentrySdk.AddBreadcrumb($"Load media details {mediaId}", "navigation", "state");

                ListEntry = listEntry;
                Media = listEntry?.Media;
                StatusMessage = string.Empty;
                ErrorDetails = string.Empty;
                IsErrorDetailsVisible = false;

                var media = await _aniListClient.GetMediaAsync(mediaId);
                if (media is null)
                {
                    StatusMessage = "Details unavailable.";
                    return;
                }

                Media = media;
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to load details. Tap Details for more.";
                ErrorDetails = _errorReportService.Record(ex, "Load details");
                IsErrorDetailsVisible = false;
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

        partial void OnMediaChanged(Media? value)
        {
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(CoverImageUrl));
        }

        partial void OnListEntryChanged(MediaListEntry? value)
            => HasListEntry = value is not null;

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
