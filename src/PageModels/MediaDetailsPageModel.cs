using AniSprinkles.Utilities;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace AniSprinkles.PageModels;

    public partial class MediaDetailsPageModel : ObservableObject
    {
        private readonly IAniListClient _aniListClient;
        private readonly IAuthService _authService;
        private readonly ErrorReportService _errorReportService;
        private readonly INavigationService _navigationService;
        private readonly ILogger<MediaDetailsPageModel> _logger;
        private int? _loadedMediaId;
        private int _loadRequestSequence;
        private int _lastRequestedMediaId;
        private MediaListEntry? _lastRequestedListEntry;

    // ── Main page state (mutually exclusive) ────────────────────────
    // Transitions:
    //   InitialLoading → Content (fetch succeeded) | Error (fetch failed / media unavailable)
    //   Content        → Content (refresh/same id) | InitialLoading (new id) | Error (refresh failed)
    //   Error          → InitialLoading (retry)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStateKey))]
    private PageState _currentState = PageState.InitialLoading;

    // StateContainer.CurrentState is typed as string; null/empty restores default
    // children (the loaded content host). Non-Content states match a StateView key.
    public string? CurrentStateKey => CurrentState == PageState.Content ? null : CurrentState.ToString();

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
    private string _errorDetails = string.Empty;

    // ── Error state (full-page error view) ──────────────────────────
    // Visibility is driven by CurrentState == PageState.Error; the following
    // properties populate the error view template.
    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorSubtitle = string.Empty;

    [ObservableProperty]
    private string _errorIconGlyph = string.Empty;

    [ObservableProperty]
    private bool _canRetry = true;

    [ObservableProperty]
    private bool _isDescriptionExpanded;

    [ObservableProperty]
    private bool _isStatusExpanded;

    [ObservableProperty]
    private double _sliderScore;

    [ObservableProperty]
    private double _sliderProgress;

    public MediaDetailsPageModel(IAniListClient aniListClient, IAuthService authService, ErrorReportService errorReportService, INavigationService navigationService, ILogger<MediaDetailsPageModel> logger)
    {
        _aniListClient = aniListClient;
        _authService = authService;
        _errorReportService = errorReportService;
        _navigationService = navigationService;
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
            : $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase((Media?.Season ?? "").ToLowerInvariant())} {Media?.SeasonYear}".Trim();

    public string DurationDisplay =>
        Media?.Duration is > 0 ? $"{Media.Duration} min/ep" : "-";

    public string SourceDisplay => string.IsNullOrWhiteSpace(Media?.Source) ? "-" : Media.Source!;

    public string CountryDisplay => string.IsNullOrWhiteSpace(Media?.CountryOfOrigin) ? "-" : Media.CountryOfOrigin!.ToUpperInvariant();

    public string AdultDisplay => Media?.IsAdult is null ? "-" : Media.IsAdult.Value ? "Adult" : "Not Adult";

    public string LicensedDisplay => Media?.IsLicensed is null ? "-" : Media.IsLicensed.Value ? "Licensed" : "Unlicensed";

    public string ReleaseWindowDisplay => FormatReleaseWindow(Media?.StartDate, Media?.EndDate);

    public string NextAiringDisplay => FormatNextAiring(Media?.NextAiringEpisode);

    public bool HasNextAiringInfo => Media?.NextAiringEpisode?.Episode is not null;

    public string NextAiringEpisodeLabel => Media?.NextAiringEpisode?.Episode is { } ep ? $"Episode {ep}" : "";

    public string NextAiringCountdownCompact
    {
        get
        {
            var seconds = Math.Max(Media?.NextAiringEpisode?.TimeUntilAiring ?? 0, 0);
            var span = TimeSpan.FromSeconds(seconds);
            var parts = new List<string>();
            if ((int)span.TotalDays > 0)
            {
                parts.Add($"{(int)span.TotalDays}d");
            }

            if (span.Hours > 0)
            {
                parts.Add($"{span.Hours}h");
            }

            if (span.Minutes > 0)
            {
                parts.Add($"{span.Minutes}m");
            }

            return parts.Count > 0 ? string.Join(" ", parts) : "now";
        }
    }

    public bool IsAiringToday
    {
        get
        {
            var seconds = Math.Max(Media?.NextAiringEpisode?.TimeUntilAiring ?? 0, 0);
            return seconds > 0 && seconds < 86400;
        }
    }

    public string NextAiringDateDisplay
    {
        get
        {
            if (Media?.NextAiringEpisode?.AiringAt is not { } airingAt)
            {
                return "";
            }

            var dt = DateTimeOffset.FromUnixTimeSeconds(airingAt).LocalDateTime;
            return dt.ToString("ddd, MMM d 'at' h:mm tt", CultureInfo.InvariantCulture);
        }
    }

    public bool HasGenres => Genres.Count > 0;

    public bool HasSynonyms => Synonyms.Count > 0;

    public string SynonymsDisplay => Synonyms.Count > 0 ? string.Join(", ", Synonyms) : "-";

    public bool HasTags => Tags.Count > 0;

    public bool HasStudios => Studios.Count > 0;

    public bool HasRankings => RankingGroups.Count > 0;

    public bool HasExternalLinks => ExternalLinks.Count > 0;

    public bool HasTrailer => !string.IsNullOrWhiteSpace(TrailerUrl);

    public bool HasMedia => Media is not null;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Media?.Description);

    // Max visible lines when the description is collapsed. Used both by DescriptionMaxLines
    // and by the IsDescriptionTruncated heuristic.
    private const int DescriptionCollapsedMaxLines = 8;

    // Approximate visible characters per line at 14sp on a typical phone (~360dp wide content area).
    // Used to estimate whether stripped visible text will exceed DescriptionCollapsedMaxLines.
    private const int DescriptionCharsPerLine = 45;

    // Paragraph/line-break count that suggests the description will overflow the line limit even
    // if its visible character count is low (e.g. several short paragraphs each wrapping 2–3 lines).
    private const int DescriptionBreakCountThreshold = 3;

    /// <summary>
    /// True when the description text likely exceeds the collapsed line limit.
    /// Uses a heuristic so the "Read more" toggle only appears when truncation
    /// actually occurs — not for every short description that exists.
    /// </summary>
    public bool IsDescriptionTruncated
    {
        get
        {
            string? description = Media?.Description;
            if (string.IsNullOrWhiteSpace(description))
            {
                return false;
            }

            // Decode HTML entities first so &amp; (5 chars) counts as & (1 char), etc.
            // This keeps the visible-char estimate aligned with what TextType="Html" actually renders.
            string decoded = WebUtility.HtmlDecode(description);

            // Estimate visible character count by skipping HTML tags.
            // Comparing visible chars to line capacity is more accurate than raw HTML length,
            // which inflates due to tag markup and gives false negatives for tag-sparse descriptions.
            int visibleChars = CountVisibleChars(decoded);
            if (visibleChars > DescriptionCollapsedMaxLines * DescriptionCharsPerLine)
            {
                return true;
            }

            // A description with several short paragraphs can overflow the line limit even when its
            // total visible character count is low, due to paragraph spacing eating visual lines.
            int breakCount = CountSubstring(decoded, "<br") + CountSubstring(decoded, "</p>");
            return breakCount >= DescriptionBreakCountThreshold;
        }
    }

    public int DescriptionMaxLines => IsDescriptionExpanded ? int.MaxValue : DescriptionCollapsedMaxLines;

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

    public string EpisodesDisplay => Media?.Episodes is > 0 ? $"{Media.Episodes} Episodes" : "";

    public string DurationPillDisplay => Media?.Duration is > 0 ? $"{Media.Duration} min/ep" : "";

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

    public string StatusIconGlyph => ListEntry?.Status switch
    {
        MediaListStatus.Current => FluentIconsRegular.Eye24,
        MediaListStatus.Planning => FluentIconsRegular.Bookmark24,
        MediaListStatus.Completed => FluentIconsRegular.CheckmarkCircle24,
        MediaListStatus.Paused => FluentIconsRegular.PauseCircle24,
        MediaListStatus.Dropped => FluentIconsRegular.DismissCircle24,
        MediaListStatus.Repeating => FluentIconsRegular.ArrowRepeatAll24,
        _ => FluentIconsRegular.AddCircle24,
    };

    // --- Progress properties ---

    /// <summary>
    /// Effective episode cap for UI: total episodes when known, next-airing episode otherwise.
    /// Sourced from the list-entry helper so My Anime and Details stay in sync.
    /// </summary>
    private int? CurrentMaxEpisodes =>
        ListEntry?.MaxEpisodes ??
        (Media?.Episodes is > 0 ? Media.Episodes :
         Media?.NextAiringEpisode?.Episode is > 0 ? Media.NextAiringEpisode.Episode :
         null);

    public string ProgressLabel
    {
        get
        {
            var progress = ListEntry?.Progress ?? 0;
            var max = CurrentMaxEpisodes;
            return max is > 0 ? $"{progress} / {max}" : $"{progress}";
        }
    }

    public double ProgressFraction
    {
        get
        {
            var max = CurrentMaxEpisodes;
            if (max is not > 0)
            {
                return 0;
            }

            return Math.Clamp((ListEntry?.Progress ?? 0) / (double)max, 0, 1);
        }
    }

    public bool HasProgressSliderMax => CurrentMaxEpisodes is > 0;

    public double ProgressSliderMax => CurrentMaxEpisodes is > 0 ? CurrentMaxEpisodes.Value : 100;

    // --- Score format properties ---
    public bool ScoreFormatIsStars => AppSettings.ScoreFormat == ScoreFormat.Point5;
    public bool ScoreFormatIsSmileys => AppSettings.ScoreFormat == ScoreFormat.Point3;
    public bool ScoreFormatIsNumeric => AppSettings.ScoreFormat is ScoreFormat.Point100 or ScoreFormat.Point10 or ScoreFormat.Point10Decimal;

    public double NumericScoreMax => AppSettings.ScoreFormat switch
    {
        ScoreFormat.Point100 => 100,
        _ => 10,
    };

    public string NumericScoreLabel
    {
        get
        {
            var score = ListEntry?.Score ?? 0;
            var max = NumericScoreMax;
            return AppSettings.ScoreFormat == ScoreFormat.Point10Decimal
                ? $"{score:0.0} / {max:0}"
                : $"{score:0} / {max:0}";
        }
    }

    public int StarRating => (int)(ListEntry?.Score ?? 0);
    public bool Star1Filled => StarRating >= 1;
    public bool Star2Filled => StarRating >= 2;
    public bool Star3Filled => StarRating >= 3;
    public bool Star4Filled => StarRating >= 4;
    public bool Star5Filled => StarRating >= 5;

    public int SmileyRating => (int)(ListEntry?.Score ?? 0);
    public bool SmileyHappySelected => SmileyRating >= 3;
    public bool SmileyNeutralSelected => SmileyRating == 2;
    public bool SmileySadSelected => SmileyRating == 1;

    public IReadOnlyList<string> Genres { get; private set; } = [];

    public IReadOnlyList<string> Synonyms { get; private set; } = [];

    public IReadOnlyList<MediaTag> Tags { get; private set; } = [];

    public IReadOnlyList<Studio> Studios { get; private set; } = [];

    public IReadOnlyList<RankingGroup> RankingGroups { get; private set; } = [];

    public IReadOnlyList<MediaExternalLink> ExternalLinks { get; private set; } = [];

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

        _logger.LogInformation(
            "MediaDetails LoadAsync enter load#{LoadRequestId} (mediaId={MediaId}, isBusy={IsBusy}, currentState={CurrentState}, loadedMediaId={LoadedMediaId}, hasListEntry={HasListEntry})",
            loadRequestId, mediaId, IsBusy, CurrentState, _loadedMediaId, listEntry is not null);

        if (IsBusy)
        {
            _logger.LogInformation("NAVTRACE load#{LoadRequestId} skipped because details view model is already busy.", loadRequestId);
            return;
        }

        if (mediaId <= 0)
        {
            _lastRequestedMediaId = mediaId;
            _lastRequestedListEntry = listEntry;
            Media = null;
            _loadedMediaId = null;
            ErrorDetails = string.Empty;
            ErrorTitle = "Details Unavailable";
            ErrorSubtitle = "The requested title could not be loaded.";
            ErrorIconGlyph = FluentIconsRegular.ErrorCircle24;
            CanRetry = false;
            CurrentState = PageState.Error;
            _logger.LogInformation("NAVTRACE load#{LoadRequestId} aborted due to invalid media id {MediaId}.", loadRequestId, mediaId);
            return;
        }

        if (_loadedMediaId == mediaId && Media is not null)
        {
            // Query attributes can be re-applied on resume/back transitions. Keep existing media and only
            // refresh list-context/error state so we avoid a second network call and full layout pass.
            // Don't overwrite ListEntry — our in-memory copy reflects any saves the user made.
            // Only accept the navigation parameter if we have no entry yet.
            if (ListEntry is null && listEntry is not null)
            {
                ListEntry = listEntry;
            }

            ErrorDetails = string.Empty;
            CurrentState = PageState.Content;
            _logger.LogInformation(
                "NAVTRACE load#{LoadRequestId} reused already-loaded media {MediaId} in {ElapsedMs}ms.",
                loadRequestId,
                mediaId,
                loadStopwatch.ElapsedMilliseconds);
            return;
        }

        // Set IsBusy immediately — before any awaits — so concurrent callers
        // are rejected by the guard above. All cleanup happens in finally.
        IsBusy = true;
        _lastRequestedMediaId = mediaId;
        _lastRequestedListEntry = listEntry;

        // Query updates can happen on the same page instance. Clear previous media for a different id
        // so we display an intentional loading state instead of stale details during transition.
        if (_loadedMediaId != mediaId)
        {
            Media = null;
        }

        try
        {
            var token = await _authService.GetAccessTokenAsync();
            IsAuthenticated = !string.IsNullOrWhiteSpace(token);
            OnPropertyChanged(nameof(CanAddToList));

            CurrentState = PageState.InitialLoading;
            _logger.LogInformation("NAVTRACE load#{LoadRequestId} starting details fetch for media {MediaId}.", loadRequestId, mediaId);
            SentrySdk.AddBreadcrumb($"Load media details {mediaId}", "navigation", "state");

            _logger.LogInformation(
                "DATATRACE load#{LoadRequestId} nav-param listEntry: Progress={Progress}, Score={Score}, EntryId={EntryId}",
                loadRequestId, listEntry?.Progress, listEntry?.Score, listEntry?.Id);
            ListEntry = listEntry;
            ErrorDetails = string.Empty;

            var fetchStopwatch = Stopwatch.StartNew();
            var result = await _aniListClient.GetMediaAsync(mediaId);
            fetchStopwatch.Stop();
            _logger.LogInformation(
                "NAVTRACE load#{LoadRequestId} media fetch completed in {FetchElapsedMs}ms for media {MediaId}.",
                loadRequestId,
                fetchStopwatch.ElapsedMilliseconds,
                mediaId);

            _logger.LogInformation(
                "DATATRACE load#{LoadRequestId} API result.ListEntry: Progress={Progress}, Score={Score}, EntryId={EntryId}",
                loadRequestId, result.ListEntry?.Progress, result.ListEntry?.Score, result.ListEntry?.Id);

            if (result.Media is null)
            {
                ErrorTitle = "Details Unavailable";
                ErrorSubtitle = "The requested title could not be loaded.";
                ErrorIconGlyph = FluentIconsRegular.ErrorCircle24;
                ErrorDetails = string.Empty;
                CanRetry = true;
                CurrentState = PageState.Error;
                _loadedMediaId = null;
                _logger.LogWarning("NAVTRACE load#{LoadRequestId} returned null media for media id {MediaId}.", loadRequestId, mediaId);
                return;
            }

            // Prefer the API-returned list entry over the navigation-passed one (it's always fresh).
            var entry = result.ListEntry ?? listEntry;
            entry?.Media = result.Media;

            _logger.LogInformation(
                "DATATRACE load#{LoadRequestId} final entry (before set): Progress={Progress}, Score={Score}, EntryId={EntryId}, Source={Source}",
                loadRequestId, entry?.Progress, entry?.Score, entry?.Id,
                result.ListEntry is not null ? "API" : "nav-param");
            ListEntry = entry;
            Media = result.Media;
            _loadedMediaId = mediaId;
            CanRetry = true;
            CurrentState = PageState.Content;
            _logger.LogInformation(
                "NAVTRACE load#{LoadRequestId} media bound in {ElapsedMs}ms for media {MediaId}.",
                loadRequestId,
                loadStopwatch.ElapsedMilliseconds,
                mediaId);
        }
        catch (Exception ex)
        {
            var apiEx = ex as AniListApiException;
            ErrorTitle = apiEx?.UserTitle ?? "Something Went Wrong";
            ErrorSubtitle = apiEx?.UserSubtitle ?? "An unexpected error occurred. Try again or check back later.";
            ErrorIconGlyph = apiEx?.IconGlyph ?? FluentIconsRegular.ErrorCircle24;
            ErrorDetails = _errorReportService.Record(ex, "Load details");
            CanRetry = true;
            CurrentState = PageState.Error;
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

    partial void OnMediaChanged(Media? value)
    {
        ApplyExtendedCollections(value);

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(CoverImageUrl));
        OnPropertyChanged(nameof(BannerImageUrl));
        OnPropertyChanged(nameof(HasBannerImage));
        OnPropertyChanged(nameof(HasMedia));
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
        OnPropertyChanged(nameof(NextAiringEpisodeLabel));
        OnPropertyChanged(nameof(NextAiringCountdownCompact));
        OnPropertyChanged(nameof(IsAiringToday));
        OnPropertyChanged(nameof(NextAiringDateDisplay));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(IsDescriptionTruncated));
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
        OnPropertyChanged(nameof(HasProgressSliderMax));
        OnPropertyChanged(nameof(ProgressSliderMax));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(ScoreFormatIsStars));
        OnPropertyChanged(nameof(ScoreFormatIsSmileys));
        OnPropertyChanged(nameof(ScoreFormatIsNumeric));
        OnPropertyChanged(nameof(NumericScoreMax));
        OnPropertyChanged(nameof(NumericScoreLabel));
    }

    private void ApplyExtendedCollections(Media? value)
    {
        Genres = value?.Genres ?? [];
        Synonyms = value?.Synonyms ?? [];
        Studios = value?.Studios ?? [];
        Tags = value?.Tags ?? [];
        RankingGroups = (value?.Rankings ?? [])
            .GroupBy(r => r.ScopeKey)
            .Select(g => new RankingGroup { Title = g.Key, Items = g.OrderBy(r => r.Rank).ToList() })
            .ToList();
        ExternalLinks = value?.ExternalLinks ?? [];
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
        OnPropertyChanged(nameof(RankingGroups));
        OnPropertyChanged(nameof(HasRankings));
        OnPropertyChanged(nameof(ExternalLinks));
        OnPropertyChanged(nameof(HasExternalLinks));
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
        _logger.LogInformation(
            "DATATRACE OnListEntryChanged: Progress={Progress}, Score={Score}, EntryId={EntryId}, MediaId={MediaId}",
            value?.Progress, value?.Score, value?.Id, value?.MediaId);

        HasListEntry = value is not null;
        OnPropertyChanged(nameof(ListStatusDisplay));
        OnPropertyChanged(nameof(CurrentStatusKey));
        OnPropertyChanged(nameof(StatusIconGlyph));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(NumericScoreLabel));
        OnPropertyChanged(nameof(StarRating));
        OnPropertyChanged(nameof(Star1Filled));
        OnPropertyChanged(nameof(Star2Filled));
        OnPropertyChanged(nameof(Star3Filled));
        OnPropertyChanged(nameof(Star4Filled));
        OnPropertyChanged(nameof(Star5Filled));
        OnPropertyChanged(nameof(SmileyRating));
        OnPropertyChanged(nameof(SmileyHappySelected));
        OnPropertyChanged(nameof(SmileyNeutralSelected));
        OnPropertyChanged(nameof(SmileySadSelected));
        SliderScore = value?.Score ?? 0;
        SliderProgress = value?.Progress ?? 0;
    }

    [RelayCommand]
    private async Task NavigateToMedia(int mediaId)
    {
        _logger.LogInformation("NAVTRACE NavigateToMedia called with mediaId={MediaId}", mediaId);
        if (mediaId <= 0)
        {
            _logger.LogWarning("NAVTRACE NavigateToMedia aborted — invalid mediaId {MediaId}", mediaId);
            return;
        }

        await _navigationService.GoToAsync("media-details", animate: false, new Dictionary<string, object>
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

    private static int CountVisibleChars(string html)
    {
        int count = 0;
        bool inTag = false;
        foreach (char c in html)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>')
            {
                inTag = false;
            }
            else if (!inTag)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountSubstring(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [RelayCommand]
    private async Task QuickSetStatus(string value)
    {
        if (Media is null || !Enum.TryParse<MediaListStatus>(value, out var status))
        {
            return;
        }

        var entry = ListEntry ?? new MediaListEntry { MediaId = Media.Id, Media = Media };
        // Ensure Media is attached so the helper can read MaxEpisodes / HasKnownEpisodeCount.
        entry.Media ??= Media;

        await ListEntryStatusFlow.ApplyStatusChangeAsync(entry, status);

        try
        {
            var saved = await _aniListClient.SaveMediaListEntryAsync(entry);
            if (saved is not null)
            {
                saved.Media = Media;
                ListEntry = saved;
                IsStatusExpanded = false;
                OnPropertyChanged(nameof(CanAddToList));
                await ShowToastAsync("Status updated");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set status for media {MediaId}.", Media.Id);
            await ShowFailureSnackbarAsync(
                ex,
                "Failed to update status. Please try again.",
                retryAction: () => _ = QuickSetStatus(value));
        }
    }

    [RelayCommand]
    private async Task RemoveFromList()
    {
        if (Media is null || ListEntry is null || ListEntry.Id == 0)
        {
            return;
        }

        // Capture before the await — DisplayAlertAsync yields and ListEntry could be
        // set to null by a concurrent refresh before we reach RemoveFromListConfirmedAsync.
        var listEntryId = ListEntry.Id;
        var title = Media.DisplayTitle ?? "this anime";
        var confirmed = await Shell.Current.CurrentPage.DisplayAlertAsync(
            "Remove from List",
            $"Remove {title} from your list?",
            "Remove",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        await RemoveFromListConfirmedAsync(listEntryId, title);
    }

    // Separated from RemoveFromList so the snackbar Retry action can re-attempt the delete
    // directly without re-showing the confirmation dialog. listEntryId and title are captured
    // as value/immutable types at failure time so a concurrent refresh cannot affect the retry.
    private async Task RemoveFromListConfirmedAsync(int listEntryId, string title)
    {
        try
        {
            var deleted = await _aniListClient.DeleteMediaListEntryAsync(listEntryId);
            if (deleted)
            {
                ListEntry = null;
                IsStatusExpanded = false;
                OnPropertyChanged(nameof(CanAddToList));
                OnPropertyChanged(nameof(HasListEntry));
                NotifyListEntryDisplayChanged();
                await ShowToastAsync($"{title} removed from list");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove media {MediaId} from list.", Media?.Id);
            await ShowFailureSnackbarAsync(
                ex,
                "Failed to remove from list. Please try again.",
                retryAction: () => _ = RemoveFromListConfirmedAsync(listEntryId, title));
        }
    }

    [RelayCommand]
    private async Task AddToList()
    {
        if (Media is null)
        {
            return;
        }

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
                await ShowToastAsync("Added to list");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add media {MediaId} to list.", Media.Id);
            await ShowFailureSnackbarAsync(
                ex,
                "Failed to add to list. Please try again.",
                retryAction: () => _ = AddToList());
        }
    }

    private CancellationTokenSource? _saveDebounceCts;
    private bool _isCompletionFlowActive;

    [RelayCommand]
    private async Task EditProgress()
    {
        if (ListEntry is null || Shell.Current is null)
        {
            return;
        }

        var max = CurrentMaxEpisodes;
        var prompt = max is > 0 ? $"Enter episode (0–{max})" : "Enter episode";
        var current = (ListEntry.Progress ?? 0).ToString();

        var input = await Shell.Current.DisplayPromptAsync(
            "Progress", prompt, "OK", "Cancel",
            initialValue: current, maxLength: 5, keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out var value))
        {
            return;
        }

        var clamped = Math.Max(0, max is > 0 ? Math.Min(value, max.Value) : value);
        await ApplyProgressChangeAsync(clamped);
    }

    [RelayCommand]
    private async Task IncrementProgress()
    {
        if (ListEntry is null)
        {
            return;
        }

        var max = CurrentMaxEpisodes ?? int.MaxValue;
        if ((ListEntry.Progress ?? 0) < max)
        {
            await ApplyProgressChangeAsync((ListEntry.Progress ?? 0) + 1);
        }
    }

    [RelayCommand]
    private async Task DecrementProgress()
    {
        if (ListEntry is null)
        {
            return;
        }

        if ((ListEntry.Progress ?? 0) > 0)
        {
            await ApplyProgressChangeAsync((ListEntry.Progress ?? 0) - 1);
        }
    }

    /// <summary>
    /// Single entry point for progress changes originating from +1 / -1 / numeric edit.
    /// Keeps the model, slider binding, and debounced save in sync, and fires the
    /// completion flow (CompletionPopup + RatingPopup) when the change lands on the
    /// known total. Slider drags route here via the snapped OnSliderProgressChanged path.
    /// </summary>
    private async Task ApplyProgressChangeAsync(int newProgress)
    {
        if (ListEntry is null)
        {
            return;
        }

        if ((ListEntry.Progress ?? 0) == newProgress)
        {
            return;
        }

        var previousProgress = ListEntry.Progress;
        ListEntry.Progress = newProgress;
        if (Math.Abs(SliderProgress - newProgress) > 0.01)
        {
            SliderProgress = newProgress;
        }

        NotifyListEntryDisplayChanged();

        if (ShouldTriggerCompletion())
        {
            if (_isCompletionFlowActive)
            {
                return;
            }

            _isCompletionFlowActive = true;
            _saveDebounceCts?.Cancel();

            try
            {
                var shouldSave = await ListEntryStatusFlow.ApplyCompletionAsync(ListEntry);
                if (shouldSave)
                {
                    NotifyListEntryDisplayChanged();
                    IsStatusExpanded = false;
                    await SaveCurrentEntryAsync();
                }
                else
                {
                    // User dismissed — revert the progress bump so the UI matches
                    // My Anime's behaviour (cancel leaves entry unchanged).
                    ListEntry.Progress = previousProgress;
                    SliderProgress = previousProgress ?? 0;
                    NotifyListEntryDisplayChanged();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Completion flow failed for media {MediaId}; falling back to normal save.", ListEntry.MediaId);
                _ = DebouncedSaveAsync();
            }
            finally
            {
                _isCompletionFlowActive = false;
            }

            return;
        }

        _ = DebouncedSaveAsync();
    }

    private bool ShouldTriggerCompletion() =>
        ListEntry is { HasKnownEpisodeCount: true, MaxEpisodes: { } max }
        && (ListEntry.Progress ?? 0) >= max
        && ListEntry.Status != MediaListStatus.Completed;

    [RelayCommand]
    private void SetStarRating(string value)
    {
        if (ListEntry is null || !int.TryParse(value, out var stars))
        {
            return;
        }
        // Tapping the same star clears the rating
        ListEntry.Score = StarRating == stars ? 0 : stars;
        NotifyListEntryDisplayChanged();
        _ = DebouncedSaveAsync();
    }

    [RelayCommand]
    private void SetSmileyRating(string value)
    {
        if (ListEntry is null || !int.TryParse(value, out var rating))
        {
            return;
        }

        ListEntry.Score = SmileyRating == rating ? 0 : rating;
        NotifyListEntryDisplayChanged();
        _ = DebouncedSaveAsync();
    }

    partial void OnSliderScoreChanged(double value)
    {
        if (ListEntry is null)
        {
            return;
        }

        var rounded = AppSettings.ScoreFormat == ScoreFormat.Point10Decimal
            ? Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2.0  // snap to 0.5 increments
            : Math.Round(value);
        // Snap the slider thumb to the nearest valid position
        if (Math.Abs(value - rounded) > 0.01)
        {
            SliderScore = rounded;
            return; // will re-enter with snapped value
        }

        if (Math.Abs((ListEntry.Score ?? 0) - rounded) < 0.01)
        {
            return;
        }

        ListEntry.Score = rounded;
        NotifyListEntryDisplayChanged();
        _ = DebouncedSaveAsync();
    }

    partial void OnSliderProgressChanged(double value)
    {
        _logger.LogInformation(
            "DATATRACE OnSliderProgressChanged: value={Value}, ListEntry.Progress={CurrentProgress}",
            value, ListEntry?.Progress);
        if (ListEntry is null)
        {
            return;
        }

        var rounded = (int)Math.Round(value);
        // Snap the slider thumb to the nearest whole number
        if (Math.Abs(value - rounded) > 0.01)
        {
            SliderProgress = rounded;
            return; // will re-enter with snapped value
        }

        if ((ListEntry.Progress ?? 0) == rounded)
        {
            return;
        }

        _ = ApplyProgressChangeAsync(rounded);
    }

    [RelayCommand]
    private void ToggleStatusExpanded()
    {
        IsStatusExpanded = !IsStatusExpanded;
    }

    private void NotifyListEntryDisplayChanged()
    {
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(NumericScoreLabel));
        OnPropertyChanged(nameof(StarRating));
        OnPropertyChanged(nameof(Star1Filled));
        OnPropertyChanged(nameof(Star2Filled));
        OnPropertyChanged(nameof(Star3Filled));
        OnPropertyChanged(nameof(Star4Filled));
        OnPropertyChanged(nameof(Star5Filled));
        OnPropertyChanged(nameof(SmileyRating));
        OnPropertyChanged(nameof(SmileyHappySelected));
        OnPropertyChanged(nameof(SmileyNeutralSelected));
        OnPropertyChanged(nameof(SmileySadSelected));
    }

    private async Task DebouncedSaveAsync()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts = new CancellationTokenSource();
        var token = _saveDebounceCts.Token;

        try
        {
            await Task.Delay(1500, token);
            await SaveCurrentEntryAsync();
        }
        catch (TaskCanceledException) { }
    }

    private async Task SaveCurrentEntryAsync()
    {
        if (Media is null || ListEntry is null)
        {
            return;
        }

        try
        {
            var saved = await _aniListClient.SaveMediaListEntryAsync(ListEntry);
            if (saved is not null)
            {
                saved.Media = Media;
                ListEntry = saved;
                await ShowToastAsync("Changes saved");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save list entry for media {MediaId}.", Media.Id);
            await ShowFailureSnackbarAsync(
                ex,
                "Failed to save changes. Please try again.",
                retryAction: () => _ = SaveCurrentEntryAsync());
        }
    }

    [RelayCommand]
    private async Task RetryLoad()
    {
        if (_lastRequestedMediaId <= 0 || IsBusy)
        {
            return;
        }

        // Flip to InitialLoading so the UI transitions to the loading spinner.
        // LoadAsync fully owns the IsBusy lifecycle — we don't touch it here.
        CurrentState = PageState.InitialLoading;
        await LoadAsync(_lastRequestedMediaId, _lastRequestedListEntry);
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
            var monthOnly = new DateOnly(date.Year.Value, date.Month.Value, 1);
            return monthOnly.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        }

        var full = new DateOnly(date.Year.Value, date.Month.Value, date.Day.Value);
        return full.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
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

    private async Task ShowSnackbarAsync(
        string message,
        Action? action = null,
        string actionText = "Retry",
        TimeSpan? duration = null)
    {
        try
        {
            var snackbar = Snackbar.Make(
                message,
                action: action,
                actionButtonText: action is null ? string.Empty : actionText,
                duration: duration ?? TimeSpan.FromSeconds(5));
            await snackbar.Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snackbar display failed");
        }
    }

    /// <summary>
    /// Shows a save-failure snackbar that adapts to the exception kind: during a
    /// service outage the global banner is already visible, so the snackbar repeats
    /// the outage title and omits the Retry action (retrying won't work for minutes
    /// or hours). All other exception kinds keep the normal retry flow.
    /// </summary>
    private Task ShowFailureSnackbarAsync(Exception ex, string fallbackMessage, Action? retryAction)
    {
        if (ex is AniListApiException { Kind: ApiErrorKind.ServiceOutage } apiEx)
        {
            return ShowSnackbarAsync(apiEx.UserTitle);
        }

        return ShowSnackbarAsync(fallbackMessage, action: retryAction);
    }

    private async Task ShowToastAsync(string message)
    {
        try
        {
            await Toast.Make(message, ToastDuration.Short).Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toast display failed");
        }
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
