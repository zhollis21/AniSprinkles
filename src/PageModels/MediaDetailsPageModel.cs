using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace AniSprinkles.PageModels;

    public partial class MediaDetailsPageModel : ObservableObject
    {
        private readonly IAniListClient _aniListClient;
        private readonly IAuthService _authService;
        private readonly ErrorReportService _errorReportService;
        private readonly ILogger<MediaDetailsPageModel> _logger;
        private int? _loadedMediaId;
        private int _loadRequestSequence;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private Media? _media;

    [ObservableProperty]
    private MediaListEntry? _listEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToList))]
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

    [ObservableProperty]
    private bool _isDescriptionExpanded;

    [ObservableProperty]
    private bool _isStatusExpanded;

    [ObservableProperty]
    private bool _isEditingListEntry;

    [ObservableProperty]
    private MediaListStatus _editStatus;

    [ObservableProperty]
    private int _editProgress;

    [ObservableProperty]
    private double _editScore;

    [ObservableProperty]
    private bool _isSavingListEntry;

    public MediaDetailsPageModel(IAniListClient aniListClient, IAuthService authService, ErrorReportService errorReportService, ILogger<MediaDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _errorReportService = errorReportService;
        _logger = logger;
    }

    public string PageTitle => Media?.DisplayTitle ?? "Details";

    public string? CoverImageUrl =>
        // A 120x170 poster does not need a "large" image payload; prefer medium to reduce decode cost on navigation.
        Media?.CoverImage?.Medium ??
        Media?.CoverImage?.Large ??
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

    public string SynonymsDisplay => Synonyms.Count > 0 ? string.Join(", ", Synonyms) : "-";

    public bool HasTags => Tags.Count > 0;

    public bool HasStudios => Studios.Count > 0;

    public bool HasRankings => Rankings.Count > 0;

    public bool HasExternalLinks => ExternalLinks.Count > 0;

    public bool HasStreamingEpisodes => StreamingEpisodes.Count > 0;

    public bool HasTrailer => !string.IsNullOrWhiteSpace(TrailerUrl);

    public bool HasMedia => Media is not null;

    public bool IsInitialLoading => Media is null && string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Media?.Description);

    public int DescriptionMaxLines => IsDescriptionExpanded ? int.MaxValue : 8;

    public string ScorePercentDisplay => Media?.AverageScore is > 0 ? $"{Media.AverageScore}%" : "--";

    public string PopularityDisplay => Media?.Popularity is > 0 ? $"{Media.Popularity:N0}" : "--";

    public string FavouritesDisplay => Media?.Favourites is > 0 ? $"{Media.Favourites:N0}" : "--";

    public string FormatDisplay => Media?.Format?.Replace("_", " ") ?? "--";

    public string StatusFormatted => Media?.Status?.Replace("_", " ") is { } s
        ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant())
        : "--";

    public string MainStudioName => Media?.Studios.FirstOrDefault(s => s.IsAnimationStudio == true)?.Name
        ?? Media?.Studios.FirstOrDefault()?.Name ?? "";

    public bool HasMainStudio => !string.IsNullOrWhiteSpace(MainStudioName);

    public string EpisodesDisplay => Media?.Episodes is > 0 ? $"{Media.Episodes} eps" : "";

    public string DurationPillDisplay => Media?.Duration is > 0 ? $"{Media.Duration} min" : "";

    public string SeasonYearDisplay => SeasonDisplay != "-" ? SeasonDisplay : "";

    public bool HasRelations => Relations.Count > 0;

    public bool HasCharacters => Characters.Count > 0;

    public bool HasRecommendations => Recommendations.Count > 0;

    public bool HasStats => ScoreDistribution.Count > 0 || StatusDistribution.Count > 0;

    public bool HasStaff => Staff.Count > 0;

    public bool IsAuthenticated { get; private set; }

    public bool CanAddToList => IsAuthenticated && !HasListEntry;

    public string ListStatusDisplay => ListEntry?.Status switch
    {
        MediaListStatus.Current => "Watching",
        MediaListStatus.Planning => "Plan to Watch",
        MediaListStatus.Completed => "Completed",
        MediaListStatus.Dropped => "Dropped",
        MediaListStatus.Paused => "Paused",
        MediaListStatus.Repeating => "Rewatching",
        _ => "Add to List",
    };

    public string CurrentStatusKey => ListEntry?.Status?.ToString() ?? "";

    public IReadOnlyList<string> Genres { get; private set; } = [];

    public IReadOnlyList<string> Synonyms { get; private set; } = [];

    public IReadOnlyList<MediaTag> Tags { get; private set; } = [];

    public IReadOnlyList<Studio> Studios { get; private set; } = [];

    public IReadOnlyList<MediaRanking> Rankings { get; private set; } = [];

    public IReadOnlyList<MediaExternalLink> ExternalLinks { get; private set; } = [];

    public IReadOnlyList<MediaStreamingEpisode> StreamingEpisodes { get; private set; } = [];

    public string? TrailerUrl { get; private set; }

    public IReadOnlyList<MediaRelationEdge> Relations { get; private set; } = [];

    public IReadOnlyList<CharacterEdge> Characters { get; private set; } = [];

    public IReadOnlyList<MediaRecommendationNode> Recommendations { get; private set; } = [];

    public IReadOnlyList<ScoreDistributionItem> ScoreDistribution { get; private set; } = [];

    public IReadOnlyList<StatusDistribution> StatusDistribution { get; private set; } = [];

    public IReadOnlyList<StaffEdge> Staff { get; private set; } = [];

    public async Task LoadAsync(int mediaId, MediaListEntry? listEntry)
    {
        var loadRequestId = Interlocked.Increment(ref _loadRequestSequence);
        var loadStopwatch = Stopwatch.StartNew();

        if (IsBusy)
        {
            _logger.LogInformation("NAVTRACE load#{LoadRequestId} skipped because details view model is already busy.", loadRequestId);
            return;
        }

        if (mediaId <= 0)
        {
            Media = null;
            _loadedMediaId = null;
            StatusMessage = "Details unavailable.";
            _logger.LogInformation("NAVTRACE load#{LoadRequestId} aborted due to invalid media id {MediaId}.", loadRequestId, mediaId);
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
            _logger.LogInformation(
                "NAVTRACE load#{LoadRequestId} reused already-loaded media {MediaId} in {ElapsedMs}ms.",
                loadRequestId,
                mediaId,
                loadStopwatch.ElapsedMilliseconds);
            return;
        }

        // Query updates can happen on the same page instance. Clear previous media for a different id
        // so we display an intentional loading state instead of stale details during transition.
        if (_loadedMediaId != mediaId)
        {
            Media = null;
        }

        var token = await _authService.GetAccessTokenAsync();
        IsAuthenticated = !string.IsNullOrWhiteSpace(token);
        OnPropertyChanged(nameof(CanAddToList));

        IsBusy = true;
        try
        {
            _logger.LogInformation("NAVTRACE load#{LoadRequestId} starting details fetch for media {MediaId}.", loadRequestId, mediaId);
            SentrySdk.AddBreadcrumb($"Load media details {mediaId}", "navigation", "state");

            ListEntry = listEntry;
            StatusMessage = string.Empty;
            ErrorDetails = string.Empty;
            IsErrorDetailsVisible = false;

            var fetchStopwatch = Stopwatch.StartNew();
            var result = await _aniListClient.GetMediaAsync(mediaId);
            fetchStopwatch.Stop();
            _logger.LogInformation(
                "NAVTRACE load#{LoadRequestId} media fetch completed in {FetchElapsedMs}ms for media {MediaId}.",
                loadRequestId,
                fetchStopwatch.ElapsedMilliseconds,
                mediaId);

            if (result.Media is null)
            {
                StatusMessage = "Details unavailable.";
                _logger.LogWarning("NAVTRACE load#{LoadRequestId} returned null media for media id {MediaId}.", loadRequestId, mediaId);
                return;
            }

            // Prefer the API-returned list entry over the navigation-passed one (it's always fresh).
            var entry = result.ListEntry ?? listEntry;
            if (entry is not null)
                entry.Media = result.Media;
            ListEntry = entry;
            Media = result.Media;
            _loadedMediaId = mediaId;
            _logger.LogInformation(
                "NAVTRACE load#{LoadRequestId} media bound in {ElapsedMs}ms for media {MediaId}.",
                loadRequestId,
                loadStopwatch.ElapsedMilliseconds,
                mediaId);
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to load details. Tap Details for more.";
            ErrorDetails = _errorReportService.Record(ex, "Load details");
            IsErrorDetailsVisible = false;
            _loadedMediaId = null;
            _logger.LogError(
                ex,
                "NAVTRACE load#{LoadRequestId} failed in {ElapsedMs}ms for media {MediaId}.",
                loadRequestId,
                loadStopwatch.ElapsedMilliseconds,
                mediaId);
        }
        finally
        {
            IsBusy = false;
            loadStopwatch.Stop();
            _logger.LogInformation(
                "NAVTRACE load#{LoadRequestId} finished in {ElapsedMs}ms for media {MediaId}.",
                loadRequestId,
                loadStopwatch.ElapsedMilliseconds,
                mediaId);
        }
    }

    partial void OnStatusMessageChanged(string value)
    {
        HasStatusMessage = !string.IsNullOrWhiteSpace(value);
        OnPropertyChanged(nameof(IsInitialLoading));
    }

    partial void OnErrorDetailsChanged(string value)
        => HasErrorDetails = !string.IsNullOrWhiteSpace(value);

    partial void OnMediaChanged(Media? value)
    {
        ApplyExtendedCollections(value);

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(CoverImageUrl));
        OnPropertyChanged(nameof(BannerImageUrl));
        OnPropertyChanged(nameof(HasBannerImage));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(IsInitialLoading));
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
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(ScorePercentDisplay));
        OnPropertyChanged(nameof(PopularityDisplay));
        OnPropertyChanged(nameof(FavouritesDisplay));
        OnPropertyChanged(nameof(FormatDisplay));
        OnPropertyChanged(nameof(StatusFormatted));
        OnPropertyChanged(nameof(MainStudioName));
        OnPropertyChanged(nameof(HasMainStudio));
        OnPropertyChanged(nameof(EpisodesDisplay));
        OnPropertyChanged(nameof(DurationPillDisplay));
        OnPropertyChanged(nameof(SeasonYearDisplay));
        OnPropertyChanged(nameof(CanAddToList));
    }

    private void ApplyExtendedCollections(Media? value)
    {
        Genres = value?.Genres ?? [];
        Synonyms = value?.Synonyms ?? [];
        Studios = value?.Studios ?? [];
        Tags = value?.Tags ?? [];
        Rankings = value?.Rankings ?? [];
        ExternalLinks = value?.ExternalLinks ?? [];
        StreamingEpisodes = value?.StreamingEpisodes ?? [];
        TrailerUrl = BuildTrailerUrl(value?.Trailer);
        Relations = value?.Relations ?? [];
        Characters = value?.Characters ?? [];
        Recommendations = value?.Recommendations ?? [];
        ScoreDistribution = value?.ScoreDistribution ?? [];
        StatusDistribution = value?.StatusDistribution ?? [];
        Staff = value?.Staff ?? [];

        OnPropertyChanged(nameof(Genres));
        OnPropertyChanged(nameof(HasGenres));
        OnPropertyChanged(nameof(Synonyms));
        OnPropertyChanged(nameof(HasSynonyms));
        OnPropertyChanged(nameof(SynonymsDisplay));
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
        OnPropertyChanged(nameof(Relations));
        OnPropertyChanged(nameof(HasRelations));
        OnPropertyChanged(nameof(Characters));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(Recommendations));
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(ScoreDistribution));
        OnPropertyChanged(nameof(StatusDistribution));
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(Staff));
        OnPropertyChanged(nameof(HasStaff));
    }

    partial void OnListEntryChanged(MediaListEntry? value)
    {
        HasListEntry = value is not null;
        OnPropertyChanged(nameof(ListStatusDisplay));
        OnPropertyChanged(nameof(CurrentStatusKey));
    }

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
    private async Task NavigateToMedia(int mediaId)
    {
        if (mediaId <= 0)
        {
            return;
        }

        await Shell.Current.GoToAsync("media-details", animate: false, new Dictionary<string, object>
        {
            ["mediaId"] = mediaId,
        });
    }

    [RelayCommand]
    private void ToggleDescription()
    {
        IsDescriptionExpanded = !IsDescriptionExpanded;
        OnPropertyChanged(nameof(DescriptionMaxLines));
    }

    [RelayCommand]
    private void ToggleStatusExpanded()
    {
        IsStatusExpanded = !IsStatusExpanded;
    }

    [RelayCommand]
    private async Task QuickSetStatus(string value)
    {
        if (Media is null || !Enum.TryParse<MediaListStatus>(value, out var status))
        {
            return;
        }

        IsSavingListEntry = true;
        try
        {
            var entry = ListEntry ?? new MediaListEntry { MediaId = Media.Id };
            entry.Status = status;

            var saved = await _aniListClient.SaveMediaListEntryAsync(entry);
            if (saved is not null)
            {
                saved.Media = Media;
                ListEntry = saved;
                IsStatusExpanded = false;
                OnPropertyChanged(nameof(CanAddToList));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set status for media {MediaId}.", Media.Id);
            StatusMessage = "Failed to update status. Try again.";
        }
        finally
        {
            IsSavingListEntry = false;
        }
    }

    [RelayCommand]
    private void EditListEntry()
    {
        if (ListEntry is null)
        {
            return;
        }

        EditStatus = ListEntry.Status ?? MediaListStatus.Current;
        EditProgress = ListEntry.Progress ?? 0;
        EditScore = ListEntry.Score ?? 0;
        IsEditingListEntry = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditingListEntry = false;
    }

    [RelayCommand]
    private async Task SaveListEntry()
    {
        if (Media is null)
        {
            return;
        }

        IsSavingListEntry = true;
        try
        {
            var entry = ListEntry ?? new MediaListEntry { MediaId = Media.Id };
            entry.Status = EditStatus;
            entry.Progress = EditProgress;
            entry.Score = EditScore;

            var saved = await _aniListClient.SaveMediaListEntryAsync(entry);
            if (saved is not null)
            {
                saved.Media = Media;
                ListEntry = saved;
                IsEditingListEntry = false;
                OnPropertyChanged(nameof(CanAddToList));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save list entry for media {MediaId}.", Media.Id);
            StatusMessage = "Failed to save. Try again.";
        }
        finally
        {
            IsSavingListEntry = false;
        }
    }

    [RelayCommand]
    private async Task AddToList()
    {
        if (Media is null)
        {
            return;
        }

        IsSavingListEntry = true;
        try
        {
            var entry = new MediaListEntry
            {
                MediaId = Media.Id,
                Status = MediaListStatus.Planning,
            };

            var saved = await _aniListClient.SaveMediaListEntryAsync(entry);
            if (saved is not null)
            {
                saved.Media = Media;
                ListEntry = saved;
                OnPropertyChanged(nameof(CanAddToList));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add media {MediaId} to list.", Media.Id);
            StatusMessage = "Failed to add to list. Try again.";
        }
        finally
        {
            IsSavingListEntry = false;
        }
    }

    [RelayCommand]
    private void SetEditStatus(string value)
    {
        if (Enum.TryParse<MediaListStatus>(value, out var status))
        {
            EditStatus = status;
        }
    }

    [RelayCommand]
    private void IncrementProgress()
    {
        var max = Media?.Episodes ?? int.MaxValue;
        if (EditProgress < max)
        {
            EditProgress++;
        }
    }

    [RelayCommand]
    private void DecrementProgress()
    {
        if (EditProgress > 0)
        {
            EditProgress--;
        }
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
