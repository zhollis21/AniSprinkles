using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace AniSprinkles.PageModels;

public partial class MediaDetailsPageModel : DetailsPageModelBase<Media>
{
    private readonly IDialogService _dialogs;
    private readonly ListEntryStatusFlow _statusFlow;

    // Correlates this page's own trace lines within one load. The in-flight guard in LoadAsync means
    // loads cannot interleave, so the base's NAVTRACE lines correlate to the current load by time.
    private int _loadRequestSequence;
    private int _loadRequestId;

    // The list entry the navigation carried, kept so RetryLoad can re-invoke with it.
    private MediaListEntry? _lastRequestedListEntry;

    // The list entry the last fetch returned, applied when its media is seeded.
    private MediaListEntry? _fetchedListEntry;

    // Per-section sort + pagination, layered on top of the heavy first-paint MediaQuery (which seeds
    // page 1 at perPage 25). Characters/Staff/Recommendations sort server-side and Load More; Relations
    // sorts entirely client-side over a fixed set (no pagination). Defaults MUST match the MediaQuery
    // sub-block sorts so the seeded page and the highlighted dropdown option agree. Mirrors
    // CharacterDetailsPageModel.
    private const int PageSize = 25;
    private const string CharactersDefaultSort = "ROLE";
    private const string StaffDefaultSort = "RELEVANCE";
    private const string RecommendationsDefaultSort = "RATING_DESC";
    private const string RelationsDefaultSort = "RELATION";

    private readonly PaginatedSection<CharacterEdge> _characters;
    private readonly PaginatedSection<StaffEdge> _staff;
    private readonly PaginatedSection<MediaRecommendationNode> _recommendations;

    // Relations is the complete set from the first-paint query; DisplayedRelations is the sorted view.
    private IReadOnlyList<MediaRelationEdge> _allRelations = [];

    // Main page state, the error-state properties and IsBusy live on DetailsPageModelBase.
    // Transitions:
    //   InitialLoading → Content (fetch succeeded) | Error (fetch failed / media unavailable)
    //   Content        → Content (refresh/same id) | InitialLoading (new id) | Error (refresh failed)
    //   Error          → InitialLoading (retry)
    [ObservableProperty]
    private Media? _media;

    [ObservableProperty]
    private MediaListEntry? _listEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToList))]
    private bool _hasListEntry;

    [ObservableProperty]
    private bool _isDescriptionExpanded;

    [ObservableProperty]
    private bool _isStatusExpanded;

    [ObservableProperty]
    private double _sliderScore;

    [ObservableProperty]
    private double _sliderProgress;

    public MediaDetailsPageModel(
        IAniListClient aniListClient,
        IAuthService authService,
        ErrorReportService errorReportService,
        INavigationService navigationService,
        IUserFeedback feedback,
        IExternalBrowser browser,
        IDialogService dialogs,
        ListEntryStatusFlow statusFlow,
        ILogger<MediaDetailsPageModel> logger)
        : base(aniListClient, authService, navigationService, feedback, browser, errorReportService, logger)
    {
        _dialogs = dialogs;
        _statusFlow = statusFlow;

        _characters = new PaginatedSection<CharacterEdge>(
            CharactersDefaultSort,
            FetchCharactersPageAsync,
            edge => edge.Node?.Id ?? 0,
            StampCharacterBadges);
        _characters.Changed += OnCharactersChanged;

        // A staff member can appear under multiple roles, so key on (id, role) to avoid the dedup
        // HashSet dropping a second role of the same person across pages.
        _staff = new PaginatedSection<StaffEdge>(
            StaffDefaultSort,
            FetchStaffPageAsync,
            edge => (edge.Node?.Id ?? 0, edge.Role ?? string.Empty),
            StampStaffBadges);
        _staff.Changed += OnStaffChanged;

        _recommendations = new PaginatedSection<MediaRecommendationNode>(
            RecommendationsDefaultSort,
            FetchRecommendationsPageAsync,
            node => node.MediaRecommendation?.Id ?? 0,
            StampRecommendationBadges);
        _recommendations.Changed += OnRecommendationsChanged;
    }

    // ── Sortable / paginated sections ───────────────────────────────
    // Bind XAML to the Displayed* collections (instances are stable for the page model's life) and to
    // the *Busy / *Sort / Has* facets. SelectedCode on the SortDropdown binds to *Sort.

    public ObservableCollection<CharacterEdge> DisplayedCharacters => _characters.Items;
    public bool CharactersBusy => _characters.IsBusy;
    public string CharactersSort => _characters.Sort;

    public ObservableCollection<StaffEdge> DisplayedStaff => _staff.Items;
    public bool StaffBusy => _staff.IsBusy;
    public string StaffSort => _staff.Sort;

    public ObservableCollection<MediaRecommendationNode> DisplayedRecommendations => _recommendations.Items;
    public bool RecommendationsBusy => _recommendations.IsBusy;
    // Recommendations have a single natural order (most recommended) so there's no sort dropdown — the
    // section just seeds/loads in RATING_DESC.

    // Relations: client-side sort only, no pagination — instant reorder, no busy/spinner.
    public ObservableCollection<MediaRelationEdge> DisplayedRelations { get; } = [];

    /// <inheritdoc />
    /// <remarks>
    /// Relations and recommendations only. The Characters and Staff carousels do show person names,
    /// and Staff Name Language does drive them since #130 — but through AniList's <c>userPreferred</c>,
    /// resolved server-side at fetch time. There is nothing local to re-project: a change to that
    /// setting invalidates the entity cache and the names arrive corrected on the next fetch.
    /// </remarks>
    protected override IEnumerable<IDisplayProjection> DisplayProjections =>
        DisplayedRelations.Cast<IDisplayProjection>().Concat(DisplayedRecommendations);

    /// <inheritdoc />
    protected override void OnDisplaySettingsChanged()
    {
        // The header title, and the rating control that switches between stars, smileys and a
        // numeric slider. None of these live on a carousel item, so DisplayProjections misses them.
        OnPropertyChanged(nameof(Media));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(ScoreFormatIsStars));
        OnPropertyChanged(nameof(ScoreFormatIsSmileys));
        OnPropertyChanged(nameof(ScoreFormatIsNumeric));
        OnPropertyChanged(nameof(NumericScoreMax));
        OnPropertyChanged(nameof(NumericScoreLabel));
    }
    public string RelationsSort { get; private set; } = RelationsDefaultSort;

    public IReadOnlyList<SortOption> CharactersSortOptions { get; } =
    [
        new SortOption { Code = "ROLE",            Display = "Role", IsSelected = true },
        new SortOption { Code = "FAVOURITES_DESC", Display = "Most Favorited" },
        new SortOption { Code = "RELEVANCE",       Display = "Featured" },
    ];

    public IReadOnlyList<SortOption> StaffSortOptions { get; } =
    [
        new SortOption { Code = "RELEVANCE",       Display = "Featured", IsSelected = true },
        new SortOption { Code = "FAVOURITES_DESC", Display = "Most Favorited" },
        new SortOption { Code = "ROLE",            Display = "Role" },
    ];

    public IReadOnlyList<SortOption> RelationsSortOptions { get; } =
    [
        new SortOption { Code = "RELATION",  Display = "Relation Type", IsSelected = true },
        new SortOption { Code = "YEAR_DESC", Display = "Newest" },
        new SortOption { Code = "YEAR_ASC",  Display = "Oldest" },
        new SortOption { Code = "TITLE",     Display = "Title" },
    ];

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

    /// <summary>
    /// True when the description text likely exceeds the collapsed line limit.
    /// Uses a heuristic so the "Read more" toggle only appears when truncation
    /// actually occurs — not for every short description that exists.
    /// </summary>
    /// <remarks>
    /// This page used to carry its own copy of the estimate and its constants. The two drifted the
    /// moment the shared one was recalibrated for Body2's real 15sp size (#138), and the label here
    /// is identical to the character and staff ones — same style, same MaxLines, same
    /// TailTruncation — so there was never a reason for them to differ.
    /// </remarks>
    public bool IsDescriptionTruncated => DescriptionTruncationHeuristic.IsTruncated(Media?.Description);

    public int DescriptionMaxLines
        => IsDescriptionExpanded ? int.MaxValue : DescriptionTruncationHeuristic.CollapsedMaxLines;

    public string ScorePercentDisplay => Media?.AverageScore is > 0 ? $"{Media.AverageScore}%" : "--";

    public string PopularityDisplay => Media?.Popularity is > 0 ? $"{Media.Popularity:N0}" : "--";

    // FavouritesDisplay lives on DetailsPageModelBase, rendering Media.FavouritesDisplay so all four
    // details pages show one format.

    public string FormatDisplay => Media?.Format?.Replace("_", " ") ?? "--";

    public string StatusFormatted => Media?.Status?.Replace("_", " ") is { } s
        ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant())
        : "--";

    public string EpisodesDisplay => CountChip(Media?.Episodes, "Episode");

    public string DurationPillDisplay => Media?.Duration is > 0 ? $"{Media.Duration} min/ep" : "";

    public string SeasonYearDisplay => SeasonDisplay != "-" ? SeasonDisplay : "";

    // The manga counterparts of the Episodes / Duration / Season chips. All three of those are
    // empty for manga (AniList returns no duration, season or episode count), and these are empty
    // for anime, so the row simply carries whichever set applies. Blank for an ongoing series,
    // which AniList leaves null until publication finishes.
    public string ChaptersDisplay => CountChip(Media?.Chapters, "Chapter");

    public string VolumesDisplay => CountChip(Media?.Volumes, "Volume");

    /// <summary>
    /// "12 Episodes" / "1 Chapter" / "" when the count is absent. Pluralised because one-shots make
    /// "1 Chapters" a visible chip rather than a theoretical one — and a single-episode anime had
    /// always read "1 Episodes" for the same reason.
    /// </summary>
    private static string CountChip(int? count, string noun) =>
        count is > 0 ? $"{count} {noun}{(count == 1 ? "" : "s")}" : "";

    public bool HasRelations => DisplayedRelations.Count > 0;

    public bool HasCharacters => _characters.Items.Count > 0;

    public bool HasRecommendations => _recommendations.Items.Count > 0;

    public bool HasStats => ScoreDistribution.Count > 0 || StatusDistribution.Count > 0;

    public bool HasStaff => _staff.Items.Count > 0;

    // IsAuthenticated, IsFavourite and CanToggleFavourite live on DetailsPageModelBase.
    public bool CanAddToList => IsAuthenticated && !HasListEntry;

    /// <summary>Which type this page is showing; drives every label that differs between the two.</summary>
    public MediaKind CurrentMediaKind => Media?.IsManga is true ? MediaKind.Manga : MediaKind.Anime;

    public string ListStatusDisplay => ListEntry?.Status is { } status
        ? MediaListVocabulary.StatusLabel(status, CurrentMediaKind)
        : "Add to List";

    // The three status-picker rows whose wording differs by type. Bound rather than hardcoded in
    // XAML so a manga page reads "Reading" / "Plan to Read" / "Rereading" (#12); the other three
    // (Completed, Paused, Dropped) are the same word for both and stay literal in the view.
    public string StatusLabelCurrent => MediaListVocabulary.StatusLabel(MediaListStatus.Current, CurrentMediaKind);

    public string StatusLabelPlanning => MediaListVocabulary.StatusLabel(MediaListStatus.Planning, CurrentMediaKind);

    public string StatusLabelRepeating => MediaListVocabulary.StatusLabel(MediaListStatus.Repeating, CurrentMediaKind);

    public string CurrentStatusKey => ListEntry?.Status?.ToString() ?? "";

    public string StatusIconGlyph => ListEntry?.Status switch
    {
        MediaListStatus.Current => Glyphs.Regular.Eye24,
        MediaListStatus.Planning => Glyphs.Regular.Bookmark24,
        MediaListStatus.Completed => Glyphs.Regular.CheckmarkCircle24,
        MediaListStatus.Paused => Glyphs.Regular.PauseCircle24,
        MediaListStatus.Dropped => Glyphs.Regular.DismissCircle24,
        MediaListStatus.Repeating => Glyphs.Regular.ArrowRepeatAll24,
        _ => Glyphs.Regular.AddCircle24,
    };

    // --- Progress properties ---

    /// <summary>
    /// Effective progress cap for UI, in <see cref="CurrentProgressUnit"/>: the list entry's own
    /// total when there is an entry, so My Anime and Details stay in sync, and otherwise the same
    /// rule derived from the media alone (needed before the media is on the user's list at all).
    /// <para>
    /// For manga the fallback is the chapter or volume count, with no airing-schedule backstop —
    /// AniList publishes none — so a still-publishing series simply has no cap.
    /// </para>
    /// </summary>
    private int? CurrentMaxProgress =>
        ListEntry?.ActiveProgressTotal ??
        (Media?.IsManga is true
            ? (Media.Chapters is > 0 ? Media.Chapters : null)
            : Media?.Episodes is > 0 ? Media.Episodes :
              Media?.NextAiringEpisode?.Episode is > 0 ? Media.NextAiringEpisode.Episode :
              null);

    /// <summary>The unit the progress control counts in — episodes, chapters, or volumes (#12).</summary>
    public MediaProgressUnit CurrentProgressUnit =>
        ListEntry?.ActiveProgressUnit
        ?? (Media?.IsManga is true ? MediaProgressUnit.Chapter : MediaProgressUnit.Episode);

    /// <summary>Singular noun for the current unit, e.g. the "Episode"/"Chapter" in the edit prompt.</summary>
    public string ProgressUnitNoun => MediaListVocabulary.UnitNoun(CurrentProgressUnit);

    private int CurrentProgressValue => ListEntry?.ActiveProgress ?? 0;

    public string ProgressLabel
    {
        get
        {
            var progress = CurrentProgressValue;
            var max = CurrentMaxProgress;
            return max is > 0 ? $"{progress} / {max}" : $"{progress}";
        }
    }

    public double ProgressFraction
    {
        get
        {
            var max = CurrentMaxProgress;
            if (max is not > 0)
            {
                return 0;
            }

            return Math.Clamp(CurrentProgressValue / (double)max, 0, 1);
        }
    }

    public bool HasProgressSliderMax => CurrentMaxProgress is > 0;

    public double ProgressSliderMax => CurrentMaxProgress is > 0 ? CurrentMaxProgress.Value : 100;

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
            // 0 in AniList means "no score recorded" — surface that explicitly so the
            // slider at the bottom doesn't read as a literal "0/10" rating.
            if (score <= 0)
            {
                return "Not rated";
            }

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

    public IReadOnlyList<ScoreDistributionItem> ScoreDistribution { get; private set; } = [];

    public IReadOnlyList<StatusDistribution> StatusDistribution { get; private set; } = [];

    // ---- Spine ------------------------------------------------------------------------------------

    protected override Media? Entity
    {
        get => Media;
        set => Media = value;
    }

    protected override string EntityNoun => "media";

    protected override string TracePrefix => "MediaDetails";

    // Type-dependent, not a constant: ToggleFavourite takes mangaId for manga, and sending a manga
    // id as animeId succeeds while favouriting nothing (#12).
    protected override FavouriteKind FavouriteKind =>
        Media?.IsManga is true ? FavouriteKind.Manga : FavouriteKind.Anime;

    protected override string? SiteUrl => Media?.SiteUrl;

    // This page's copy is about the title rather than the entity kind.
    protected override (string Title, string Subtitle) InvalidIdError
        => ("Details Unavailable", "The requested title could not be loaded.");

    // A null result here means the query came back empty rather than 404'd, which a retry can fix —
    // unlike the other three details pages, where a missing entity is final.
    protected override (string Title, string Subtitle) NotFoundError
        => ("Details Unavailable", "The requested title could not be loaded.");

    protected override bool NullResultIsRetryable => true;

    protected override (string Title, string Subtitle) FallbackLoadError
        => ("Something Went Wrong", "An unexpected error occurred. Try again or check back later.");

    /// <param name="listEntry">The viewer's list entry as carried by the navigation, when there is one.
    /// The fetched entry wins over it — it is always fresher.</param>
    /// <remarks>
    /// Unlike the other three details pages, a second load while one is in flight is dropped rather
    /// than superseding it: this page's load is the heavy one and its list-entry merge is
    /// order-sensitive. The guard lives here rather than in a <c>ShouldSkipLoad</c> override so that
    /// nothing this load owns — the list entry, the trace id — is written until it has been accepted.
    /// A dropped load that had already overwritten them would hand the wrong list context to the load
    /// still running, and renumber its remaining trace lines.
    /// </remarks>
    public Task LoadAsync(int mediaId, MediaListEntry? listEntry)
    {
        var requestId = Interlocked.Increment(ref _loadRequestSequence);

        Logger.LogInformation(
            "MediaDetails LoadAsync enter load#{LoadRequestId} (mediaId={MediaId}, isBusy={IsBusy}, currentState={CurrentState}, loadedMediaId={LoadedMediaId}, hasListEntry={HasListEntry})",
            requestId, mediaId, IsBusy, CurrentState, LoadedId, listEntry is not null);

        if (IsBusy)
        {
            Logger.LogInformation("NAVTRACE load#{LoadRequestId} skipped because details view model is already busy.", requestId);
            return Task.CompletedTask;
        }

        _loadRequestId = requestId;
        _lastRequestedListEntry = listEntry;
        return LoadCoreAsync(mediaId);
    }

    // Query attributes can be re-applied on resume/back transitions. Keep the existing media and only
    // refresh list-context/error state so we avoid a second network call and full layout pass. Don't
    // overwrite ListEntry — our in-memory copy reflects any saves the user made. Only accept the
    // navigation parameter if we have no entry yet.
    protected override void OnEntityReused()
    {
        if (ListEntry is null && _lastRequestedListEntry is not null)
        {
            ListEntry = _lastRequestedListEntry;
        }

        ErrorDetails = string.Empty;
        Logger.LogInformation("NAVTRACE load#{LoadRequestId} reused already-loaded media {MediaId}.", _loadRequestId, LoadedId);
    }

    protected override void OnLoadStarting(int id)
    {
        SentrySdk.AddBreadcrumb($"Load media details {id}", "navigation", "state");

        Logger.LogInformation(
            "DATATRACE load#{LoadRequestId} nav-param listEntry: Progress={Progress}, Score={Score}, EntryId={EntryId}",
            _loadRequestId, _lastRequestedListEntry?.Progress, _lastRequestedListEntry?.Score, _lastRequestedListEntry?.Id);

        ListEntry = _lastRequestedListEntry;
        ErrorDetails = string.Empty;
    }

    protected override void OnAuthenticationResolved() => OnPropertyChanged(nameof(CanAddToList));

    protected override async Task<Media?> FetchAsync(int id, CancellationToken cancellationToken)
    {
        var fetchStopwatch = Stopwatch.StartNew();
        var result = await AniList.GetMediaAsync(id, cancellationToken);
        fetchStopwatch.Stop();

        Logger.LogInformation(
            "NAVTRACE load#{LoadRequestId} media fetch completed in {FetchElapsedMs}ms for media {MediaId}.",
            _loadRequestId, fetchStopwatch.ElapsedMilliseconds, id);

        Logger.LogInformation(
            "DATATRACE load#{LoadRequestId} API result.ListEntry: Progress={Progress}, Score={Score}, EntryId={EntryId}",
            _loadRequestId, result.ListEntry?.Progress, result.ListEntry?.Score, result.ListEntry?.Id);

        _fetchedListEntry = result.ListEntry;
        return result.Media;
    }

    protected override void SeedSections(Media entity)
    {
        // Prefer the API-returned list entry over the navigation-passed one (it's always fresh).
        var entry = _fetchedListEntry ?? _lastRequestedListEntry;
        entry?.Media = entity;

        Logger.LogInformation(
            "DATATRACE load#{LoadRequestId} final entry (before set): Progress={Progress}, Score={Score}, EntryId={EntryId}, Source={Source}",
            _loadRequestId, entry?.Progress, entry?.Score, entry?.Id,
            _fetchedListEntry is not null ? "API" : "nav-param");

        ListEntry = entry;
    }

    protected override void ResetForNewEntity() => _fetchedListEntry = null;

    protected override string DescribeSeededSections()
        => $"{_characters.Items.Count} characters, {_staff.Items.Count} staff, {_recommendations.Items.Count} recommendations";

    protected override Task RetryLoadCore()
    {
        if (LastRequestedId <= 0 || IsBusy)
        {
            return Task.CompletedTask;
        }

        return LoadAsync(LastRequestedId, _lastRequestedListEntry);
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
        OnPropertyChanged(nameof(IsFavourite));
        OnPropertyChanged(nameof(HasFavourites));
        ToggleFavouriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FormatDisplay));
        OnPropertyChanged(nameof(StatusFormatted));
        OnPropertyChanged(nameof(EpisodesDisplay));
        OnPropertyChanged(nameof(DurationPillDisplay));
        OnPropertyChanged(nameof(SeasonYearDisplay));
        OnPropertyChanged(nameof(ChaptersDisplay));
        OnPropertyChanged(nameof(VolumesDisplay));
        OnPropertyChanged(nameof(CurrentMediaKind));
        OnPropertyChanged(nameof(CurrentProgressUnit));
        OnPropertyChanged(nameof(ProgressUnitNoun));
        OnPropertyChanged(nameof(ListStatusDisplay));
        OnPropertyChanged(nameof(StatusLabelCurrent));
        OnPropertyChanged(nameof(StatusLabelPlanning));
        OnPropertyChanged(nameof(StatusLabelRepeating));
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
        Studios = BuildStudioChips(value?.Studios);
        Tags = value?.Tags ?? [];
        RankingGroups = (value?.Rankings ?? [])
            .GroupBy(r => r.ScopeKey)
            .Select(g => new RankingGroup { Title = g.Key, Items = g.OrderBy(r => r.Rank).ToList() })
            .ToList();
        ExternalLinks = value?.ExternalLinks ?? [];
        TrailerUrl = BuildTrailerUrl(value?.Trailer);
        ScoreDistribution = value?.ScoreDistribution ?? [];
        StatusDistribution = value?.StatusDistribution ?? [];

        // Seed the paginated sections from the heavy first-page query and reset their dropdowns to the
        // default sort. Seed() resets each section first, so a null Media (set during a new-id transition)
        // clears them. Each section's Changed event drives its Has*/Busy/Sort notifications.
        _characters.Seed(value?.Characters ?? [], value?.CharactersPageInfo);
        _staff.Seed(value?.Staff ?? [], value?.StaffPageInfo);
        _recommendations.Seed(value?.Recommendations ?? [], value?.RecommendationsPageInfo);
        SyncCharactersSortSelection(CharactersDefaultSort);
        SyncStaffSortSelection(StaffDefaultSort);

        // Relations sorts entirely client-side over a fixed set; capture it and apply the default order.
        _allRelations = value?.Relations ?? [];
        RelationsSort = RelationsDefaultSort;
        SyncRelationsSortSelection(RelationsDefaultSort);
        ApplyRelationsSort();

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
        OnPropertyChanged(nameof(HasRelations));
        OnPropertyChanged(nameof(RelationsSort));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(CharactersSort));
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(ScoreDistribution));
        OnPropertyChanged(nameof(StatusDistribution));
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(HasStaff));
        OnPropertyChanged(nameof(StaffSort));
    }

    // ── Section sort + pagination ────────────────────────────────────
    //
    // The main load and section ops run under a page-scoped CTS that OnDisappearing cancels, so we stop
    // hitting the API once the user navigates away (matches Character/Staff details). PaginatedSection's
    // generation guard stays as defense-in-depth (it drops stale responses when a new-media load re-seeds).
    // The sort dropdown is a CommunityToolkit popup that fires the host page's OnDisappearing, which would
    // cancel the very sort it's about to request — so list ops go through Scope.EnsureActive(), which recreates
    // a cancelled scope while still on the page; the same-id reuse guard in LoadAsync keeps the popup's
    // OnAppearing reload a no-op.

    private Task<(IReadOnlyList<CharacterEdge> Items, PageInfo? PageInfo)> FetchCharactersPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => AniList.LoadMediaCharactersPageAsync(LoadedId, page, sort, PageSize, cancellationToken);

    private Task<(IReadOnlyList<StaffEdge> Items, PageInfo? PageInfo)> FetchStaffPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => AniList.LoadMediaStaffPageAsync(LoadedId, page, sort, PageSize, cancellationToken);

    private Task<(IReadOnlyList<MediaRecommendationNode> Items, PageInfo? PageInfo)> FetchRecommendationsPageAsync(
        int page, string sort, CancellationToken cancellationToken)
        => AniList.LoadMediaRecommendationsPageAsync(LoadedId, page, sort, PageSize, cancellationToken);

    // --- Characters ---

    // CanExecute gates the scroll-threshold trigger so RemainingItemsThresholdReached can't re-invoke
    // while a fetch/sort is in flight or once fully paged (matches CharacterDetailsPageModel).
    [RelayCommand(CanExecute = nameof(CanLoadMoreCharacters))]
    private Task LoadMoreCharacters()
        => ListOps.RunAsync(
            "Characters · Load More",
            "media",
            LoadedId,
            () => _characters.LoadMoreAsync(Scope.EnsureActive()),
            () => _characters.Items.Count);

    private bool CanLoadMoreCharacters() => _characters.CanLoadMore;

    [RelayCommand]
    private Task SelectCharactersSort(string? code)
        => SelectSectionSortAsync(code, _characters, CharactersSortOptions, "Characters");

    // --- Staff ---

    [RelayCommand(CanExecute = nameof(CanLoadMoreStaff))]
    private Task LoadMoreStaff()
        => ListOps.RunAsync(
            "Staff · Load More",
            "media",
            LoadedId,
            () => _staff.LoadMoreAsync(Scope.EnsureActive()),
            () => _staff.Items.Count);

    private bool CanLoadMoreStaff() => _staff.CanLoadMore;

    [RelayCommand]
    private Task SelectStaffSort(string? code)
        => SelectSectionSortAsync(code, _staff, StaffSortOptions, "Staff");

    // --- Recommendations ---

    [RelayCommand(CanExecute = nameof(CanLoadMoreRecommendations))]
    private Task LoadMoreRecommendations()
        => ListOps.RunAsync(
            "Recommendations · Load More",
            "media",
            LoadedId,
            () => _recommendations.LoadMoreAsync(Scope.EnsureActive()),
            () => _recommendations.Items.Count);

    private bool CanLoadMoreRecommendations() => _recommendations.CanLoadMore;

    // Shared sort-change flow for the server-paginated sections: re-fetch page 1 with the new
    // sort, and once it settles re-sync the dropdown highlight to the sort that actually took effect
    // (a failed change leaves the old sort, so the highlight reverts).
    private Task SelectSectionSortAsync<T>(string? code, PaginatedSection<T> section, IReadOnlyList<SortOption> options, string label)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, section.Sort, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return ListOps.RunAsync(
            $"{label} · sort→{code}",
            "media",
            LoadedId,
            () => section.ChangeSortAsync(code, Scope.EnsureActive()),
            () => section.Items.Count,
            onComplete: () => SyncSortSelection(options, section.Sort));
    }

    // --- Relations (client-side sort, no pagination) ---

    [RelayCommand]
    private void SelectRelationsSort(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, RelationsSort, StringComparison.Ordinal))
        {
            return;
        }

        RelationsSort = code;
        OnPropertyChanged(nameof(RelationsSort));
        SyncRelationsSortSelection(code);
        ApplyRelationsSort();
    }

    private void ApplyRelationsSort()
    {
        var sorted = DetailsListSorters.SortRelations(RelationsSort, _allRelations);
        DisplayedRelations.Clear();
        foreach (var edge in sorted)
        {
            // Stamp the year badge before adding (relations always show their year).
            edge.MetricBadge = BuildRelationBadge(edge.Node);
            DisplayedRelations.Add(edge);
        }

        OnPropertyChanged(nameof(HasRelations));
    }

    // ── Metric badges ───────────────────────────────────────────────
    // Each Media section has a single natural metric, so its card always shows it (a heart + favourites for
    // Characters/Staff, 👍 + rating for Recommendations, 📅 + year for Relations) regardless of the active
    // sort. (The detail pages, which offer several metric sorts, keep their badge sort-dependent instead.)
    // Stamping runs via PaginatedSection.onItemsAdded (Seed / Load More / sort refetch); Relations stamps in
    // ApplyRelationsSort. The `sort` arg is required by the onItemsAdded delegate but unused here.

    private static void StampCharacterBadges(IReadOnlyList<CharacterEdge> items, string sort)
    {
        foreach (var edge in items)
        {
            edge.MetricBadge = BuildCharacterBadge(edge.Node);
        }
    }

    private static void StampStaffBadges(IReadOnlyList<StaffEdge> items, string sort)
    {
        foreach (var edge in items)
        {
            edge.MetricBadge = BuildStaffBadge(edge.Node);
        }
    }

    private static void StampRecommendationBadges(IReadOnlyList<MediaRecommendationNode> items, string sort)
    {
        foreach (var node in items)
        {
            node.MetricBadge = BuildRecommendationBadge(node);
        }
    }

    // The metric is always shown (a blank card reads as broken), so missing counts render as "0" and a
    // missing year as "—" (the app's existing empty-value convention). A null node — a degenerate edge —
    // is the only case with no badge at all.
    private static ItemMetricBadge? BuildCharacterBadge(Character? node) =>
        node is null ? null : FavouritesBadge(node.HasFavourites ? node.FavouritesDisplay : "0");

    private static ItemMetricBadge? BuildStaffBadge(StaffNode? node) =>
        node is null ? null : FavouritesBadge(node.HasFavourites ? node.FavouritesDisplay : "0");

    private static ItemMetricBadge BuildRecommendationBadge(MediaRecommendationNode node) =>
        new()
        {
            Glyph = Glyphs.Regular.ThumbLike24,
            IconColor = Color.FromArgb("#34C759"),
            Text = node.HasRating ? node.RatingDisplay : "0",
        };

    private static ItemMetricBadge? BuildRelationBadge(RelatedMedia? node) =>
        node is null
            ? null
            : new ItemMetricBadge
            {
                Glyph = Glyphs.Regular.Calendar24,
                IconColor = Color.FromArgb("#00C2FF"),
                Text = node.HasYear ? node.YearDisplay : "—",
            };

    private static ItemMetricBadge FavouritesBadge(string text) => new()
    {
        Glyph = Glyphs.Regular.Heart24,
        IconColor = Color.FromArgb("#FF2D95"),
        Text = text,
    };

    // --- Changed handlers + selection sync ---

    private void OnCharactersChanged()
    {
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(CharactersBusy));
        OnPropertyChanged(nameof(CharactersSort));
        LoadMoreCharactersCommand.NotifyCanExecuteChanged();
    }

    private void OnStaffChanged()
    {
        OnPropertyChanged(nameof(HasStaff));
        OnPropertyChanged(nameof(StaffBusy));
        OnPropertyChanged(nameof(StaffSort));
        LoadMoreStaffCommand.NotifyCanExecuteChanged();
    }

    private void OnRecommendationsChanged()
    {
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(RecommendationsBusy));
        LoadMoreRecommendationsCommand.NotifyCanExecuteChanged();
    }

    private void SyncCharactersSortSelection(string code) => SyncSortSelection(CharactersSortOptions, code);
    private void SyncStaffSortSelection(string code) => SyncSortSelection(StaffSortOptions, code);
    private void SyncRelationsSortSelection(string code) => SyncSortSelection(RelationsSortOptions, code);

    private static void SyncSortSelection(IReadOnlyList<SortOption> options, string code)
    {
        foreach (var opt in options)
        {
            opt.IsSelected = string.Equals(opt.Code, code, StringComparison.Ordinal);
        }
    }

    partial void OnListEntryChanged(MediaListEntry? value)
    {
        Logger.LogInformation(
            "DATATRACE OnListEntryChanged: Progress={Progress}, Score={Score}, EntryId={EntryId}, MediaId={MediaId}",
            value?.Progress, value?.Score, value?.Id, value?.MediaId);

        HasListEntry = value is not null;
        OnPropertyChanged(nameof(ListStatusDisplay));
        OnPropertyChanged(nameof(CurrentStatusKey));
        OnPropertyChanged(nameof(StatusIconGlyph));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(HasProgressSliderMax));
        OnPropertyChanged(nameof(ProgressSliderMax));
        OnPropertyChanged(nameof(CurrentProgressUnit));
        OnPropertyChanged(nameof(ProgressUnitNoun));
        OnPropertyChanged(nameof(ListStatusDisplay));
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
    private async Task NavigateToCharacter(int characterId)
    {
        Logger.LogInformation("NAVTRACE NavigateToCharacter called with characterId={CharacterId}", characterId);
        if (characterId <= 0)
        {
            Logger.LogWarning("NAVTRACE NavigateToCharacter aborted — invalid characterId {CharacterId}", characterId);
            return;
        }

        await NavigationService.GoToAsync("character-details", animate: false, new Dictionary<string, object>
        {
            ["characterId"] = characterId,
        });
    }

    [RelayCommand]
    private async Task NavigateToStaff(int staffId)
    {
        Logger.LogInformation("NAVTRACE NavigateToStaff called with staffId={StaffId}", staffId);
        if (staffId <= 0)
        {
            Logger.LogWarning("NAVTRACE NavigateToStaff aborted — invalid staffId {StaffId}", staffId);
            return;
        }

        await NavigationService.GoToAsync("staff-details", animate: false, new Dictionary<string, object>
        {
            ["staffId"] = staffId,
        });
    }

    [RelayCommand]
    private async Task NavigateToStudio(int studioId)
    {
        Logger.LogInformation("NAVTRACE NavigateToStudio called with studioId={StudioId}", studioId);
        if (studioId <= 0)
        {
            Logger.LogWarning("NAVTRACE NavigateToStudio aborted — invalid studioId {StudioId}", studioId);
            return;
        }

        await NavigationService.GoToAsync("studio-details", animate: false, new Dictionary<string, object>
        {
            ["studioId"] = studioId,
        });
    }

    [RelayCommand]
    private void ToggleDescription()
    {
        IsDescriptionExpanded = !IsDescriptionExpanded;
        OnPropertyChanged(nameof(DescriptionMaxLines));
    }

    [RelayCommand]
    private async Task QuickSetStatus(string value)
    {
        if (Media is null || !Enum.TryParse<MediaListStatus>(value, out var status))
        {
            return;
        }

        var entry = ListEntry ?? new MediaListEntry { MediaId = Media.Id, Media = Media };
        // Ensure Media is attached so the helper can read ActiveProgressTotal / HasKnownProgressTotal.
        entry.Media ??= Media;

        await _statusFlow.ApplyStatusChangeAsync(entry, status);

        try
        {
            var saved = await AniList.SaveMediaListEntryAsync(entry);
            if (saved is not null)
            {
                saved.Media = Media;
                ListEntry = saved;
                IsStatusExpanded = false;
                OnPropertyChanged(nameof(CanAddToList));
                await Feedback.ShowToastAsync("Status updated");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set status for media {MediaId}.", Media.Id);
            await Feedback.ShowFailureSnackbarAsync(
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

        // Capture before the await — the popup yields and ListEntry could be set
        // to null by a concurrent refresh before we reach RemoveFromListConfirmedAsync.
        var listEntryId = ListEntry.Id;
        var title = Media.DisplayTitle ?? "this anime";
        var confirmed = await _dialogs.ConfirmAsync(
            title: "Remove from List",
            message: $"Remove {title} from your list?",
            confirmText: "Remove",
            isDestructive: true,
            iconGlyph: Glyphs.Regular.Delete24);

        if (!confirmed)
        {
            return;
        }

        SentrySdk.AddBreadcrumb($"Remove from list confirmed (Details, entry {listEntryId})", "list", "user");

        await RemoveFromListConfirmedAsync(listEntryId, title);
    }

    // Separated from RemoveFromList so the snackbar Retry action can re-attempt the delete
    // directly without re-showing the confirmation dialog. listEntryId and title are captured
    // as value/immutable types at failure time so a concurrent refresh cannot affect the retry.
    private async Task RemoveFromListConfirmedAsync(int listEntryId, string title)
    {
        try
        {
            var deleted = await AniList.DeleteMediaListEntryAsync(listEntryId);
            if (deleted)
            {
                ListEntry = null;
                IsStatusExpanded = false;
                OnPropertyChanged(nameof(CanAddToList));
                OnPropertyChanged(nameof(HasListEntry));
                NotifyListEntryDisplayChanged();
                await Feedback.ShowToastAsync($"{title} removed from list");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to remove media {MediaId} from list.", Media?.Id);
            await Feedback.ShowFailureSnackbarAsync(
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

            var saved = await AniList.SaveMediaListEntryAsync(entry);
            if (saved is not null)
            {
                saved.Media = Media;
                ListEntry = saved;
                OnPropertyChanged(nameof(CanAddToList));
                await Feedback.ShowToastAsync("Added to list");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to add media {MediaId} to list.", Media.Id);
            await Feedback.ShowFailureSnackbarAsync(
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
        if (ListEntry is null)
        {
            return;
        }

        var max = CurrentMaxProgress;
        var noun = ProgressUnitNoun.ToLowerInvariant();
        var prompt = max is > 0 ? $"Enter {noun} (0–{max})" : $"Enter {noun}";
        var current = CurrentProgressValue.ToString();

        var input = await _dialogs.PromptAsync(
            "Progress", prompt, initialValue: current, maxLength: 5, numericKeyboard: true);

        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out var value))
        {
            return;
        }

        var clamped = ListEntry.ClampProgress(value);
        await ApplyProgressChangeAsync(clamped);
    }

    [RelayCommand]
    private async Task IncrementProgress()
    {
        if (ListEntry is null)
        {
            return;
        }

        var max = CurrentMaxProgress ?? int.MaxValue;
        if (CurrentProgressValue < max)
        {
            await ApplyProgressChangeAsync(CurrentProgressValue + 1);
        }
    }

    [RelayCommand]
    private async Task DecrementProgress()
    {
        if (ListEntry is null)
        {
            return;
        }

        if (CurrentProgressValue > 0)
        {
            await ApplyProgressChangeAsync(CurrentProgressValue - 1);
        }
    }

    /// <summary>
    /// Single entry point for progress changes originating from +1 / -1 / numeric edit.
    /// Keeps the model, slider binding, and debounced save in sync, and fires the
    /// completion flow (ConfirmPopup + RatingPopup) when the change lands on the
    /// known total. Slider drags route here via the snapped OnSliderProgressChanged path.
    /// </summary>
    private async Task ApplyProgressChangeAsync(int newProgress)
    {
        if (ListEntry is null)
        {
            return;
        }

        if (CurrentProgressValue == newProgress)
        {
            return;
        }

        // Capture the unit before writing. It is derived from the counters (#12), so setting a
        // volume count to 0 flips the entry back to chapters — and a revert below that went through
        // the *new* unit would rewrite the wrong field.
        var unit = CurrentProgressUnit;
        var previousProgress = ListEntry.ProgressFor(unit);
        ListEntry.SetProgressFor(unit, newProgress);
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
                var shouldSave = await _statusFlow.ApplyCompletionAsync(ListEntry);
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
                    ListEntry.SetProgressFor(unit, previousProgress);
                    SliderProgress = previousProgress ?? 0;
                    NotifyListEntryDisplayChanged();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Completion flow failed for media {MediaId}; reverting optimistic progress change.", ListEntry.MediaId);
                // Treat popup failure the same as user cancel — don't persist a
                // completion the user never confirmed.
                ListEntry.SetProgressFor(unit, previousProgress);
                SliderProgress = previousProgress ?? 0;
                NotifyListEntryDisplayChanged();
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
        ListEntry?.IsCompletionAt(ListEntry.ActiveProgress ?? 0) ?? false;

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
        Logger.LogInformation(
            "DATATRACE OnSliderProgressChanged: value={Value}, active={CurrentProgress} {Unit}",
            value, ListEntry?.ActiveProgress, CurrentProgressUnit);
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

        if (CurrentProgressValue == rounded)
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
            var saved = await AniList.SaveMediaListEntryAsync(ListEntry);
            if (saved is not null)
            {
                saved.Media = Media;
                ListEntry = saved;
                await Feedback.ShowToastAsync("Changes saved");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save list entry for media {MediaId}.", Media.Id);
            await Feedback.ShowFailureSnackbarAsync(
                ex,
                "Failed to save changes. Please try again.",
                retryAction: () => _ = SaveCurrentEntryAsync());
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

    private static IReadOnlyList<Studio> BuildStudioChips(IReadOnlyList<Studio>? studios)
    {
        if (studios is null || studios.Count == 0)
        {
            return [];
        }

        var candidates = studios.Where(s => s.IsAnimationStudio == true).ToList();
        if (candidates.Count == 0)
        {
            candidates = studios.ToList();
        }

        var result = candidates
            .Where(s => s.Id > 0 && !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .OrderByDescending(s => s.IsMain == true)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Only call out the main studio when there's more than one — with a single studio it's implied.
        if (result.Count > 1)
        {
            foreach (var studio in result)
            {
                studio.ShowMainStudioLabel = studio.IsMain == true;
            }
        }

        return result;
    }
}
