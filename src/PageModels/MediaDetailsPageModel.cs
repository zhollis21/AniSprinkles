using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace AniSprinkles.PageModels;

    public partial class MediaDetailsPageModel : ObservableObject
    {
        private readonly IAniListClient _aniListClient;
        private readonly ErrorReportService _errorReportService;
        private readonly ILogger<MediaDetailsPageModel> _logger;
        // Tracks the last fully loaded media to avoid duplicate fetch/rebind when the same query is applied again.
        private int? _loadedMediaId;

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
        Media?.CoverImage?.Large ??
        Media?.CoverImage?.Medium ??
        Media?.CoverImage?.ExtraLarge;

    public string? BannerImageUrl => Media?.BannerImage;

    public bool HasBannerImage => !string.IsNullOrWhiteSpace(BannerImageUrl);

    public bool HasMalId => Media?.IdMal is not null;

    public string SeasonDisplay =>
        string.IsNullOrWhiteSpace(Media?.Season) && Media?.SeasonYear is null
            ? "-"
            : $"{Media?.Season?.ToUpperInvariant()} {Media?.SeasonYear}".Trim();

    public string DurationDisplay =>
        Media?.Duration is > 0 ? $"{Media.Duration} min/ep" : "-";

    public string SourceDisplay => string.IsNullOrWhiteSpace(Media?.Source) ? "-" : Media.Source!;

    public string CountryDisplay => string.IsNullOrWhiteSpace(Media?.CountryOfOrigin) ? "-" : Media.CountryOfOrigin!.ToUpperInvariant();

    public string AdultDisplay => Media?.IsAdult is null ? "-" : Media.IsAdult.Value ? "Adult" : "Not Adult";

    public string LicensedDisplay => Media?.IsLicensed is null ? "-" : Media.IsLicensed.Value ? "Licensed" : "Unlicensed";

    public string ReleaseWindowDisplay => FormatReleaseWindow(Media?.StartDate, Media?.EndDate);

    public string NextAiringDisplay => FormatNextAiring(Media?.NextAiringEpisode);

    public bool HasNextAiringInfo => Media?.NextAiringEpisode?.Episode is not null;

    public bool HasGenres => Genres.Count > 0;

    public bool HasSynonyms => Synonyms.Count > 0;

    public bool HasTags => Tags.Count > 0;

    public bool HasStudios => Studios.Count > 0;

    public bool HasRankings => Rankings.Count > 0;

    public bool HasExternalLinks => ExternalLinks.Count > 0;

    public bool HasStreamingEpisodes => StreamingEpisodes.Count > 0;

    public bool HasTrailer => !string.IsNullOrWhiteSpace(TrailerUrl);

        public IReadOnlyList<string> Genres { get; private set; } = [];

        public IReadOnlyList<string> Synonyms { get; private set; } = [];

        public IReadOnlyList<MediaTag> Tags { get; private set; } = [];

        public IReadOnlyList<Studio> Studios { get; private set; } = [];

        public IReadOnlyList<MediaRanking> Rankings { get; private set; } = [];

        public IReadOnlyList<MediaExternalLink> ExternalLinks { get; private set; } = [];

        public IReadOnlyList<MediaStreamingEpisode> StreamingEpisodes { get; private set; } = [];

        public string? TrailerUrl { get; private set; }

    public async Task LoadAsync(int mediaId, MediaListEntry? listEntry)
    {
        if (IsBusy)
        {
            return;
        }

        if (mediaId <= 0)
        {
            StatusMessage = "Details unavailable.";
            return;
        }

        if (_loadedMediaId == mediaId && Media is not null)
        {
            // Query attributes can be re-applied on resume/back transitions. Keep existing media and only
            // refresh list-context/error state so we avoid a second network call and full layout pass.
            ListEntry = listEntry;
            StatusMessage = string.Empty;
            ErrorDetails = string.Empty;
            IsErrorDetailsVisible = false;
            return;
        }

        IsBusy = true;
        try
        {
            _logger.LogInformation("Loading details for media {MediaId}.", mediaId);
            SentrySdk.AddBreadcrumb($"Load media details {mediaId}", "navigation", "state");

            ListEntry = listEntry;
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
            _loadedMediaId = mediaId;
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to load details. Tap Details for more.";
            ErrorDetails = _errorReportService.Record(ex, "Load details");
            IsErrorDetailsVisible = false;
            _loadedMediaId = null;
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
        // These collections are pre-shaped in AniListClient so this UI-thread handler stays lightweight.
        Genres = value is null ? Array.Empty<string>() : value.Genres;
        Synonyms = value is null ? Array.Empty<string>() : value.Synonyms;
        Studios = value is null ? Array.Empty<Studio>() : value.Studios;
        Tags = value is null ? Array.Empty<MediaTag>() : value.Tags;
        Rankings = value is null ? Array.Empty<MediaRanking>() : value.Rankings;
        ExternalLinks = value is null ? Array.Empty<MediaExternalLink>() : value.ExternalLinks;
        StreamingEpisodes = value is null ? Array.Empty<MediaStreamingEpisode>() : value.StreamingEpisodes;
        TrailerUrl = BuildTrailerUrl(value?.Trailer);

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(CoverImageUrl));
        OnPropertyChanged(nameof(BannerImageUrl));
        OnPropertyChanged(nameof(HasBannerImage));
        OnPropertyChanged(nameof(HasMalId));
        OnPropertyChanged(nameof(SeasonDisplay));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(SourceDisplay));
        OnPropertyChanged(nameof(CountryDisplay));
        OnPropertyChanged(nameof(AdultDisplay));
        OnPropertyChanged(nameof(LicensedDisplay));
        OnPropertyChanged(nameof(ReleaseWindowDisplay));
        OnPropertyChanged(nameof(NextAiringDisplay));
        OnPropertyChanged(nameof(HasNextAiringInfo));
        OnPropertyChanged(nameof(Genres));
        OnPropertyChanged(nameof(HasGenres));
        OnPropertyChanged(nameof(Synonyms));
        OnPropertyChanged(nameof(HasSynonyms));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(Studios));
        OnPropertyChanged(nameof(HasStudios));
        OnPropertyChanged(nameof(Rankings));
        OnPropertyChanged(nameof(HasRankings));
        OnPropertyChanged(nameof(ExternalLinks));
        OnPropertyChanged(nameof(HasExternalLinks));
        OnPropertyChanged(nameof(StreamingEpisodes));
        OnPropertyChanged(nameof(HasStreamingEpisodes));
        OnPropertyChanged(nameof(TrailerUrl));
        OnPropertyChanged(nameof(HasTrailer));
    }

    partial void OnListEntryChanged(MediaListEntry? value)
        => HasListEntry = value is not null;

    [RelayCommand]
    private void ToggleDetails()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        IsErrorDetailsVisible = !IsErrorDetailsVisible;
    }

    [RelayCommand]
    private async Task CopyError()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(ErrorDetails);
        StatusMessage = "Error details copied.";
    }

    [RelayCommand]
    private async Task ShareError()
    {
        if (!HasErrorDetails)
        {
            return;
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = ErrorDetails,
            Title = "AniSprinkles Error Details"
        });
    }

    [RelayCommand]
    private static async Task OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Launcher.Default.OpenAsync(uri);
        }
    }

    private static string FormatReleaseWindow(MediaDate? start, MediaDate? end)
    {
        var startDisplay = FormatDate(start);
        var endDisplay = FormatDate(end);

        if (string.IsNullOrWhiteSpace(startDisplay) && string.IsNullOrWhiteSpace(endDisplay))
        {
            return "-";
        }

        if (string.IsNullOrWhiteSpace(endDisplay))
        {
            return $"{startDisplay} -> ?";
        }

        return $"{startDisplay} -> {endDisplay}";
    }

    private static string FormatDate(MediaDate? date)
    {
        if (date is null || date.Year is null)
        {
            return string.Empty;
        }

        if (date.Month is null)
        {
            return date.Year.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (date.Day is null)
        {
            return $"{date.Year:D4}-{date.Month:D2}";
        }

        return $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";
    }

    private static string FormatNextAiring(MediaAiringEpisode? next)
    {
        if (next?.Episode is null)
        {
            return "-";
        }

        var seconds = Math.Max(next.TimeUntilAiring ?? 0, 0);
        var span = TimeSpan.FromSeconds(seconds);
        var countdown = span.TotalDays >= 1
            ? $"{(int)span.TotalDays}d {span.Hours}h"
            : $"{span.Hours}h {span.Minutes}m";

        return $"Episode {next.Episode} in {countdown}";
    }

    private static string? BuildTrailerUrl(MediaTrailer? trailer)
    {
        if (string.IsNullOrWhiteSpace(trailer?.Id) || string.IsNullOrWhiteSpace(trailer.Site))
        {
            return null;
        }

        return trailer.Site.ToLowerInvariant() switch
        {
            "youtube" => $"https://www.youtube.com/watch?v={trailer.Id}",
            "dailymotion" => $"https://www.dailymotion.com/video/{trailer.Id}",
            _ => null
        };
    }
}
