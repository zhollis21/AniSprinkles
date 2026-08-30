#if CI
namespace AniSprinkles.Services;

/// <summary>
/// CI-only stub that returns hardcoded anime data so screenshot builds show a fully
/// authenticated, populated UI without needing a real AniList OAuth token.
/// Compiled out of Debug and Release builds entirely — only active when -p:CiBuild=true.
/// </summary>
internal sealed class CIAniListClient : IAniListClient
{
    /// <inheritdoc />
    /// <remarks>No-op: the fixtures are static, so there is nothing to invalidate.</remarks>
    public void InvalidateEntityCache() { }

    public Task<AniListUser> GetViewerAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(StubData.Viewer);

    public Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>> GetMediaListGroupedAsync(
        MediaKind kind, CancellationToken cancellationToken = default)
        => Task.FromResult(kind == MediaKind.Manga ? StubData.MangaGroupedList : StubData.GroupedList);

    public Task<IReadOnlyList<MediaListEntry>> GetMediaListAsync(
        MediaKind kind, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MediaListEntry> flat =
            (kind == MediaKind.Manga ? StubData.MangaGroupedList : StubData.GroupedList)
            .SelectMany(g => g.Entries)
            .ToList();
        return Task.FromResult(flat);
    }

    public Task<(Media? Media, MediaListEntry? ListEntry)> GetMediaAsync(
        int id, CancellationToken cancellationToken = default)
    {
        // Manga is searched alongside the anime list, not folded into it: the real Media(id:) query
        // is type-agnostic, so any id the app can reach must resolve here too (#12). Before this,
        // the three manga ids already sitting in the anime fixtures — Attack on Titan's relations
        // and One Piece's manga on Luffy's character page — resolved to nothing, which turned every
        // manga tap into an error page the moment those taps started navigating.
        var entry = StubData.GroupedList
            .SelectMany(g => g.Entries)
            .Concat(StubData.MangaEntries)
            .FirstOrDefault(e => e.MediaId == id);
        if (entry is not null)
        {
            return Task.FromResult<(Media?, MediaListEntry?)>((entry.Media, entry));
        }

        // Media with no list entry, which is the only way to reach the details page's "Add to List".
        var offList = StubData.OffListMedia.FirstOrDefault(m => m.Id == id);
        return Task.FromResult<(Media?, MediaListEntry?)>((offList, null));
    }

    public Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> SearchMediaPageAsync(
        string search, MediaKind? kind, bool? isAdult = false, int page = 1, int perPage = 20, CancellationToken cancellationToken = default)
    {
        // Honours isAdult the way the real endpoint does — SearchPageModel pins it per result set,
        // and the canary is what proves that pin still holds in CI. Both types apply the filter:
        // AniList takes the same isAdult argument either way, and modelling it on one side only
        // would leave the pin unproven for half the app.
        //
        // A null kind is the All pill, and it has to mean "both" here exactly as it does over the
        // wire, where the argument is omitted rather than sent as null (#12).
        IReadOnlyList<BrowseMediaItem> source = kind switch
        {
            MediaKind.Manga => StubData.MangaBrowseItemsFor(isAdult),
            MediaKind.Anime => StubData.BrowseItemsFor(isAdult),
            _ => [.. StubData.BrowseItemsFor(isAdult), .. StubData.MangaBrowseItemsFor(isAdult)],
        };

        IReadOnlyList<BrowseMediaItem> items = source
            .Where(i => i.Node?.DisplayTitle.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        return Task.FromResult((items, (PageInfo?)new PageInfo { HasNextPage = false, CurrentPage = page }));
    }

    public Task<DiscoverSections> GetDiscoverSectionsAsync(
        string currentSeason, int currentSeasonYear, string nextSeason, int nextSeasonYear,
        bool filterAdult, bool includeAdultSections, int perPage = 20, CancellationToken cancellationToken = default)
        => Task.FromResult(new DiscoverSections
        {
            // filterAdult was previously ignored here, which meant the SFW rows were never actually
            // filtered in CI. Honouring it is what lets the canary detect a broken filter.
            Airing = StubSectionPage(filterAdult),
            Trending = StubSectionPage(filterAdult),
            Top = StubSectionPage(filterAdult),
            TopMovies = StubSectionPage(filterAdult),
            AllTimePopular = StubSectionPage(filterAdult),
            Upcoming = StubSectionPage(filterAdult),
            // The 18+ pair is section-pinned rather than toggle-driven, so it serves the canary —
            // and is only requested at all when the toggle is on, which CI never does.
            PopularAdult = includeAdultSections ? AdultSectionPage() : DiscoverSectionPage.Empty,
            TopRatedAdult = includeAdultSections ? AdultSectionPage() : DiscoverSectionPage.Empty,
        });

    // filterAdult true = SFW only; false = the toggle is on, so nothing is filtered out.
    private static DiscoverSectionPage StubSectionPage(bool filterAdult)
        => new(StubData.BrowseItemsFor(filterAdult ? false : null), new PageInfo { HasNextPage = false, CurrentPage = 1 });

    private static DiscoverSectionPage AdultSectionPage()
        => new(StubData.BrowseItemsFor(true), new PageInfo { HasNextPage = false, CurrentPage = 1 });

    public Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> BrowseAnimePageAsync(
        string sort, string? status = null, string? season = null, int? seasonYear = null, bool? isAdult = null,
        string? format = null, int page = 1, int perPage = 25, CancellationToken ct = default)
        // Discover row paging and View All both land here through DiscoverSectionFetch, which pins
        // isAdult to the value its page 1 was seeded under (#118). Honouring the argument is what
        // makes a Load More that fetched under the wrong policy show up as a canary in the capture.
        => Task.FromResult((StubData.BrowseItemsFor(isAdult), (PageInfo?)new PageInfo { HasNextPage = false, CurrentPage = page }));

    public Task<MediaListEntry?> SaveMediaListEntryAsync(
        MediaListEntry entry, CancellationToken cancellationToken = default)
        => Task.FromResult<MediaListEntry?>(entry);

    public Task<bool> DeleteMediaListEntryAsync(int entryId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> ToggleFavouriteAsync(FavouriteKind kind, int id, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(StubData.Viewer.Id);

    public Task<AniListUser> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        // Echo the update, the way AniList's real UpdateUser mutation returns the updated viewer.
        //
        // Returning the untouched static viewer made every Display Preferences change revert on the
        // next Settings load, which made the whole section unverifiable in a CI build: the page model
        // confirms the save — clearing the pending marker that protects a local change — and then
        // syncs from this response, which still reported the pre-change value. Found while verifying
        // #130, where picking a staff name language snapped straight back to Western.
        var options = StubData.Viewer.Options;

        if (request.TitleLanguage.HasValue) { options.TitleLanguage = request.TitleLanguage.Value; }
        if (request.StaffNameLanguage.HasValue) { options.StaffNameLanguage = request.StaffNameLanguage.Value; }
        if (request.AiringNotifications.HasValue) { options.AiringNotifications = request.AiringNotifications.Value; }
        if (request.RestrictMessagesToFollowing.HasValue) { options.RestrictMessagesToFollowing = request.RestrictMessagesToFollowing.Value; }
        if (request.ActivityMergeTime.HasValue) { options.ActivityMergeTime = request.ActivityMergeTime.Value; }
        if (request.ScoreFormat.HasValue) { StubData.Viewer.ScoreFormat = request.ScoreFormat.Value; }

        if (request.NotificationOptions is { Count: > 0 } notifications)
        {
            options.NotificationOptions =
            [
                .. notifications.Select(n => new NotificationOption { Type = n.Type, Enabled = n.Enabled }),
            ];
        }

        // DisplayAdultContent is deliberately NOT echoed. The adult-content canary (see AGENTS.md)
        // rests on the stub viewer reporting the toggle off so every surface must filter the flagged
        // fixture out; making that reversible from inside the app would put the safety gate at the
        // mercy of whatever a capture run happens to tap.
        return Task.FromResult(StubData.Viewer);
    }

    public Task<Staff?> GetStaffAsync(
        int id,
        string charactersSort = "FAVOURITES_DESC",
        string mediaSort = "POPULARITY_DESC",
        int charactersPage = 1,
        int mediaPage = 1,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Staff?>(StubData.Staff);

    public Task<Character?> GetCharacterAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Character?>(StubData.Character);

    public Task<Studio?> GetStudioAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        int mediaPerPage = 25,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Studio?>(StubData.Studio);

    public Task<(IReadOnlyList<StaffCharacterEdge> Items, PageInfo? PageInfo)> LoadStaffCharactersPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<StaffCharacterEdge>, PageInfo?)>(
            ([], new PageInfo { HasNextPage = false, CurrentPage = page }));

    public Task<(IReadOnlyList<StaffMediaEdge> Items, PageInfo? PageInfo)> LoadStaffMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<StaffMediaEdge>, PageInfo?)>(
            ([], new PageInfo { HasNextPage = false, CurrentPage = page }));

    public Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> LoadCharacterMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<CharacterMediaEdge>, PageInfo?)>(
            ([], new PageInfo { HasNextPage = false, CurrentPage = page }));

    public Task<(IReadOnlyList<StudioMediaEdge> Items, PageInfo? PageInfo)> LoadStudioMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<StudioMediaEdge>, PageInfo?)>(
            ([], new PageInfo { HasNextPage = false, CurrentPage = page }));

    public Task<(IReadOnlyList<CharacterEdge> Items, PageInfo? PageInfo)> LoadMediaCharactersPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<CharacterEdge>, PageInfo?)>(
            ([], new PageInfo { HasNextPage = false, CurrentPage = page }));

    public Task<(IReadOnlyList<StaffEdge> Items, PageInfo? PageInfo)> LoadMediaStaffPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<StaffEdge>, PageInfo?)>(
            ([], new PageInfo { HasNextPage = false, CurrentPage = page }));

    public Task<(IReadOnlyList<MediaRecommendationNode> Items, PageInfo? PageInfo)> LoadMediaRecommendationsPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken ct = default)
        => Task.FromResult<(IReadOnlyList<MediaRecommendationNode>, PageInfo?)>(
            ([], new PageInfo { HasNextPage = false, CurrentPage = page }));

    public Task<IReadOnlyList<AiringScheduleEntry>> GetAiringScheduleAsync(
        IReadOnlyList<int> mediaIds, int airingAfter, int airingBefore, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<AiringScheduleEntry> entries =
        [
            new AiringScheduleEntry
            {
                Id = 1,
                AiringAt = (int)(now - TimeSpan.FromMinutes(10)).ToUnixTimeSeconds(),
                Episode = 1120,
                MediaId = 21,
                MediaTitle = "One Piece",
                CoverImageUrl = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21-YCDoj1EkAxFn.jpg",
            },
            new AiringScheduleEntry
            {
                Id = 2,
                AiringAt = (int)(now - TimeSpan.FromMinutes(5)).ToUnixTimeSeconds(),
                Episode = 4,
                MediaId = 145064,
                MediaTitle = "Jujutsu Kaisen",
                CoverImageUrl = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx145064-5OEswA46AS4c.jpg",
            },
        ];
        return Task.FromResult(entries);
    }

    // ---------------------------------------------------------------------------
    // Stub data — built once, shared across all method calls.
    // Media IDs, cover URLs, scores, and metadata are real AniList data.
    // Progress and list scores are illustrative.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="MediaAiringEpisode"/> where <see cref="MediaAiringEpisode.TimeUntilAiring"/>
    /// is always derived from the same <paramref name="airingTime"/> as <see cref="MediaAiringEpisode.AiringAt"/>,
    /// so the two fields are always consistent with each other.
    /// </summary>
    private static MediaAiringEpisode MakeAiringEpisode(int episode, DateTimeOffset airingTime)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaAiringEpisode
        {
            Episode = episode,
            AiringAt = (int)airingTime.ToUnixTimeSeconds(),
            TimeUntilAiring = (int)Math.Max((airingTime - now).TotalSeconds, 0),
        };
    }

    private static class StubData
    {
        public static readonly AniListUser Viewer = new()
        {
            Id = 999999,
            Name = "CIUser",
            AvatarLarge = "https://s4.anilist.co/file/anilistcdn/user/avatar/large/b7720462-zNg9PalTCPjL.jpg",
            BannerImage = "https://s4.anilist.co/file/anilistcdn/user/banner/b7720462-imnzaFvIFTem.jpg",
            ScoreFormat = ScoreFormat.Point10Decimal,
            AnimeSectionOrder = ["Watching", "Planning", "Completed", "Dropped", "Paused", "Repeating"],
            // The manga list has its own names (#12): Reading/Rereading where anime says
            // Watching/Rewatching. Ordering the manga tab by the anime list would sort every
            // section against a name it never contains.
            MangaSectionOrder = ["Reading", "Rereading", "Completed", "Paused", "Dropped", "Planning"],
            Options = new UserOptions
            {
                TitleLanguage = UserTitleLanguage.Romaji,
                AiringNotifications = true,
                ProfileColor = "blue",
            },
            AnimeStatistics = new UserAnimeStatistics
            {
                Count = 10,
                EpisodesWatched = 1038,
                MinutesWatched = 24912,
                MeanScore = 8.75,
            },
        };

        // ── Currently Watching ───────────────────────────────────────────────────

        private static readonly MediaListEntry OnePiece = new()
        {
            Id = 1001, MediaId = 21, Status = MediaListStatus.Current, Progress = 800, Score = 8.0,
            Media = new Media
            {
                Id = 21, Format = "TV", Episodes = null, AverageScore = 87, MeanScore = 88,
                Popularity = 641_752, Favourites = 90_457,
                Status = "RELEASING", Season = "FALL", SeasonYear = 1999, Source = "MANGA",
                Title = new MediaTitle { Romaji = "ONE PIECE", English = "ONE PIECE", Native = "ワンピース" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21-ELSYx3yMPcKM.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx21-ELSYx3yMPcKM.jpg",
                    Color = "#e49335",
                },
                BannerImage = "https://s4.anilist.co/file/anilistcdn/media/anime/banner/21-wf37VakJmZqs.jpg",
                Description = "Gol D. Roger was known as the \"Pirate King,\" the strongest and most infamous being to have sailed the Grand Line. The capture and execution of Roger by the World Government brought a change throughout the world. His last words before his death revealed the existence of the greatest treasure in the world, One Piece. It was this revelation that brought about the Grand Age of Pirates, men who dreamed of finding One Piece—which promises an unlimited amount of riches and fame—and quite possibly the pinnacle of glory and the title of the Pirate King.<br><br>Enter Monkey D. Luffy, a 17-year-old boy who defies your standard definition of a pirate. Rather than the popular persona of a wicked, hardened, toothless pirate ransacking villages for fun, Luffy's reason for being a pirate is one of pure wonder: the thought of an exciting adventure that leads him to intriguing people and ultimately, the promised treasure.",
                StartDate = new MediaDate { Year = 1999, Month = 10, Day = 20 },
                Genres = ["Action", "Adventure", "Comedy", "Drama", "Fantasy"],
                // Toei is One Piece's real (sole) animation studio; the two secondary entries are synthetic
                // so the Media Details "Studios" section renders multiple cards in the CI screenshot.
                Studios =
                [
                    new Studio { Id = 18, Name = "Toei Animation", IsAnimationStudio = true, IsMain = true, Favourites = 8_730 },
                    new Studio { Id = 11, Name = "Madhouse", IsAnimationStudio = true, IsMain = false, Favourites = 31_200 },
                    new Studio { Id = 1, Name = "Studio Pierrot", IsAnimationStudio = true, IsMain = false, Favourites = 9_800 },
                ],
                // Airs today in 3 hours — exercises the short countdown airing path
                NextAiringEpisode = MakeAiringEpisode(1160, DateTimeOffset.UtcNow.AddHours(3)),
                // Main Straw Hats (real AniList ids + JP seiyuu). Tapping Luffy deep-links to the
                // character page; tapping his VA from there reaches the staff page.
                Characters =
                [
                    Cast(40, "Monkey D. Luffy", "https://s4.anilist.co/file/anilistcdn/character/large/b40-MNypXsxSRb1R.png", 95075, "Mayumi Tanaka", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95075-1qD4TeW1ON92.png"),
                    Cast(62, "Roronoa Zoro", "https://s4.anilist.co/file/anilistcdn/character/large/b62-S7oAeA9WInjV.png", 95123, "Kazuya Nakai", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95123-54LrTiD9kGwY.jpg"),
                    Cast(723, "Nami", "https://s4.anilist.co/file/anilistcdn/character/large/b723-vp5hPptgnNEC.png", 95076, "Akemi Okamura", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95076-itRGy8F3x5Em.png"),
                    Cast(305, "Sanji", "https://s4.anilist.co/file/anilistcdn/character/large/b305-6lisPmHtCnLT.png", 95125, "Hiroaki Hirata", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95125-NeFFiJupoDVj.png"),
                    Cast(309, "Tony Tony Chopper", "https://s4.anilist.co/file/anilistcdn/character/large/b309-H64NhbJ2ywIQ.jpg", 95128, "Ikue Otani", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95128-9YWpE1d2U8Sj.png"),
                    Cast(724, "Usopp", "https://s4.anilist.co/file/anilistcdn/character/large/b724-GFGgI9AJQkfy.jpg", 95067, "Kappei Yamaguchi", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95067-hqIpNxMfAuN2.png"),
                    Cast(61, "Nico Robin", "https://s4.anilist.co/file/anilistcdn/character/large/b61-ywXUyyocEEqt.png", 95130, "Yuriko Yamaguchi", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95130-GoO41ve3YWQw.png"),
                    Cast(64, "Franky", "https://s4.anilist.co/file/anilistcdn/character/large/n64-ChX6ZzHHjXqA.png", 95131, "Kazuki Yao", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95131-TCVTgxb08tfE.png"),
                ],
            },
        };

        private static readonly MediaListEntry AttackOnTitan = new()
        {
            Id = 1002, MediaId = 16498, Status = MediaListStatus.Current, Progress = 20,
            Media = new Media
            {
                Id = 16498, Format = "TV", Episodes = 25, AverageScore = 85,
                Status = "FINISHED", Season = "SPRING", SeasonYear = 2013,
                Title = new MediaTitle { Romaji = "Shingeki no Kyojin", English = "Attack on Titan" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx16498-buvcRTBx4NSm.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx16498-buvcRTBx4NSm.jpg",
                    Color = "#f1a143",
                },
                Genres = ["Action", "Drama", "Fantasy", "Mystery"],
                Relations =
                [
                    new MediaRelationEdge
                    {
                        RelationType = "SEQUEL",
                        Node = new RelatedMedia
                        {
                            Id = 20958, Format = "TV", Type = "ANIME",
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin Season 2", English = "Attack on Titan Season 2" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx20958-a5eG9qsMswfe.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx20958-a5eG9qsMswfe.jpg",
                            },
                        },
                    },
                    new MediaRelationEdge
                    {
                        RelationType = "SIDE_STORY",
                        Node = new RelatedMedia
                        {
                            Id = 18397, Format = "OVA", Type = "ANIME",
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin OVA", English = "Attack on Titan OVA" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx18397-2uHo4QPLCXWM.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx18397-2uHo4QPLCXWM.jpg",
                            },
                        },
                    },
                    new MediaRelationEdge
                    {
                        RelationType = "SOURCE",
                        Node = new RelatedMedia
                        {
                            Id = 53390, Format = "MANGA", Type = "MANGA",
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin", English = "Attack on Titan" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx53390-1RsuABC34P9D.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx53390-1RsuABC34P9D.jpg",
                            },
                        },
                    },
                    new MediaRelationEdge
                    {
                        RelationType = "ALTERNATIVE",
                        Node = new RelatedMedia
                        {
                            Id = 99147, Format = "MOVIE", Type = "ANIME",
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin Movie 1", English = "Attack on Titan: Crimson Bow and Arrow" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx99147-bMZz0xPGWMMi.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx99147-bMZz0xPGWMMi.jpg",
                            },
                        },
                    },
                    new MediaRelationEdge
                    {
                        RelationType = "SPIN_OFF",
                        Node = new RelatedMedia
                        {
                            Id = 85199, Format = "MANGA", Type = "MANGA",
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin Gaiden: Kuinaki Sentaku", English = "Attack on Titan: No Regrets" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx85199-IzuYa59zXTBN.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx85199-IzuYa59zXTBN.jpg",
                            },
                        },
                    },
                ],
            },
        };

        private static readonly MediaListEntry JujutsuKaisen = new()
        {
            Id = 1003, MediaId = 113415, Status = MediaListStatus.Current, Progress = 15,
            Media = new Media
            {
                Id = 113415, Format = "TV", Episodes = 24, AverageScore = 85,
                Status = "FINISHED", Season = "FALL", SeasonYear = 2020,
                Title = new MediaTitle { Romaji = "Jujutsu Kaisen", English = "JUJUTSU KAISEN" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx113415-LHBAeoZDIsnF.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx113415-LHBAeoZDIsnF.jpg",
                    Color = "#e45d5d",
                },
                Genres = ["Action", "Drama", "Supernatural"],
            },
        };

        private static readonly MediaListEntry HunterXHunter = new()
        {
            Id = 1004, MediaId = 11061, Status = MediaListStatus.Current, Progress = 75,
            Media = new Media
            {
                Id = 11061, Format = "TV", Episodes = 148, AverageScore = 89,
                Status = "FINISHED", Season = "FALL", SeasonYear = 2011,
                Title = new MediaTitle { Romaji = "HUNTER×HUNTER (2011)", English = "Hunter x Hunter (2011)" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx11061-y5gsT1hoHuHw.png",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx11061-y5gsT1hoHuHw.png",
                    Color = "#f1d65d",
                },
                Genres = ["Action", "Adventure", "Fantasy"],
                // Airs in ~1 month — exercises the long countdown airing path
                NextAiringEpisode = MakeAiringEpisode(149, DateTimeOffset.UtcNow.AddDays(30)),
            },
        };

        // ── Completed ────────────────────────────────────────────────────────────

        private static readonly MediaListEntry FmaB = new()
        {
            Id = 1005, MediaId = 5114, Status = MediaListStatus.Completed, Progress = 64, Score = 9.5,
            Media = new Media
            {
                Id = 5114, Format = "TV", Episodes = 64, AverageScore = 90,
                Status = "FINISHED", Season = "SPRING", SeasonYear = 2009,
                Title = new MediaTitle { Romaji = "Hagane no Renkinjutsushi: FULLMETAL ALCHEMIST", English = "Fullmetal Alchemist: Brotherhood" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx5114-nSWCgQlmOMtj.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx5114-nSWCgQlmOMtj.jpg",
                    Color = "#e4c993",
                },
                Genres = ["Action", "Adventure", "Drama", "Fantasy"],
            },
        };

        private static readonly MediaListEntry DeathNote = new()
        {
            Id = 1006, MediaId = 1535, Status = MediaListStatus.Completed, Progress = 37, Score = 9.0,
            Media = new Media
            {
                Id = 1535, Format = "TV", Episodes = 37, AverageScore = 84,
                Status = "FINISHED", Season = "FALL", SeasonYear = 2006,
                Title = new MediaTitle { Romaji = "DEATH NOTE", English = "Death Note" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx1535-kUgkcrfOrkUM.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx1535-kUgkcrfOrkUM.jpg",
                    Color = "#3d3d3d",
                },
                Genres = ["Mystery", "Psychological", "Supernatural", "Thriller"],
            },
        };

        private static readonly MediaListEntry ASilentVoice = new()
        {
            Id = 1007, MediaId = 20954, Status = MediaListStatus.Completed, Progress = 1, Score = 8.5,
            Media = new Media
            {
                Id = 20954, Format = "MOVIE", Episodes = 1, AverageScore = 88,
                Status = "FINISHED", Season = "SUMMER", SeasonYear = 2016,
                Title = new MediaTitle { Romaji = "Koe no Katachi", English = "A Silent Voice" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx20954-sYRfE5jQRtSB.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx20954-sYRfE5jQRtSB.jpg",
                    Color = "#5dbbe4",
                },
                Genres = ["Drama", "Romance", "Slice of Life"],
            },
        };

        private static readonly MediaListEntry DemonSlayer = new()
        {
            Id = 1008, MediaId = 101922, Status = MediaListStatus.Completed, Progress = 26, Score = 8.0,
            Media = new Media
            {
                Id = 101922, Format = "TV", Episodes = 26, AverageScore = 83,
                Status = "FINISHED", Season = "SPRING", SeasonYear = 2019,
                Title = new MediaTitle { Romaji = "Kimetsu no Yaiba", English = "Demon Slayer: Kimetsu no Yaiba" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx101922-WBsBl0ClmgYL.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx101922-WBsBl0ClmgYL.jpg",
                    Color = "#f1c9ae",
                },
                Genres = ["Action", "Adventure", "Drama", "Fantasy", "Supernatural"],
            },
        };

        // ── Planning ─────────────────────────────────────────────────────────────

        private static readonly MediaListEntry YourName = new()
        {
            Id = 1009, MediaId = 21519, Status = MediaListStatus.Planning, Progress = 0,
            Media = new Media
            {
                Id = 21519, Format = "MOVIE", Episodes = 1, AverageScore = 86,
                Status = "FINISHED", Season = "SUMMER", SeasonYear = 2016,
                Title = new MediaTitle { Romaji = "Kimi no Na wa.", English = "Your Name." },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21519-SUo3ZQuCbYhJ.png",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx21519-SUo3ZQuCbYhJ.png",
                    Color = "#0da1e4",
                },
                Genres = ["Drama", "Romance", "Supernatural"],
            },
        };

        private static readonly MediaListEntry PromisedNeverland = new()
        {
            Id = 1010, MediaId = 101759, Status = MediaListStatus.Planning, Progress = 0,
            Media = new Media
            {
                Id = 101759, Format = "TV", Episodes = 12, AverageScore = 84,
                Status = "FINISHED", Season = "WINTER", SeasonYear = 2019,
                Title = new MediaTitle { Romaji = "Yakusoku no Neverland", English = "The Promised Neverland" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx101759-8UR7r9MNVpz2.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx101759-8UR7r9MNVpz2.jpg",
                    Color = "#e4ae50",
                },
                Genres = ["Drama", "Fantasy", "Horror", "Mystery", "Psychological", "Thriller"],
            },
        };

        // ── Adult-filter canary ─────────────────────────────────────────────────────────────────
        //
        // A fixture flagged IsAdult that must NEVER reach the screen in CI, because the stub viewer
        // leaves DisplayAdultContent off. It carries no adult content of its own — the title is a
        // shouted marker and the cover is deliberately unset so the app draws its own placeholder.
        //
        // The point is that the adult filter was previously unexercised end to end: with no flagged
        // fixture anywhere, the filtering in DiscoverSectionFetch, the browse/search queries and
        // MediaListSectionsMerger could all have broken without changing a single screenshot. If
        // this title appears in a capture, or in the UI-dump gate the workflow runs against it, the
        // filter is broken and the same defect would be exposing real 18+ content on a real account.
        public const string AdultCanaryTitle = "18PLUS CANARY - FILTER FAILED";

        private static readonly MediaListEntry AdultCanary = new()
        {
            Id = 1099, MediaId = 999_001, Status = MediaListStatus.Current, Progress = 1,
            Media = new Media
            {
                Id = 999_001, Format = "TV", Episodes = 1, IsAdult = true,
                Status = "FINISHED", Season = "WINTER", SeasonYear = 2020,
                Title = new MediaTitle { Romaji = AdultCanaryTitle, English = AdultCanaryTitle },
                // No CoverImage on purpose: ImageUrl.IsReal treats null as "no image", so the app
                // renders its own placeholder rather than fetching anything.
                Genres = ["Hentai"],
            },
        };

        // ── Manga (#12) ──────────────────────────────────────────────────────────
        // Deliberately NOT in GroupedList: the Library tab is still anime-only until the manga list
        // lands, so these are reached through Search, a relations carousel, or a character page.
        //
        // The ids are not arbitrary. Three of them were already referenced by existing fixtures and
        // resolved to nothing, so every manga tap in CI landed on the error page the moment #12 made
        // those taps navigate instead of toast: 53390 and 85199 sit in Attack on Titan’s relations,
        // and 30013 is One Piece's manga on Luffy's character page.
        //
        // Between them the set covers each shape the progress layer has to tell apart: a finished
        // chapter-tracked series, a still-publishing volume-tracked one with no cap at all, an entry
        // with BOTH counters set, two off-list entries (one NOVEL, one ONE_SHOT), and an 18+ canary.

        /// <summary>
        /// The flagship manga fixture — reached from Attack on Titan's relations carousel, which is
        /// the shortest path to a manga details page in the app. Finished and chapter-tracked, so it
        /// has a real cap and can drive the completion flow. Carries the sections One Piece carries
        /// plus the ones no fixture had before (tags, rankings, external links, stats), because the
        /// manga details page is what this change actually has to be looked at on.
        /// </summary>
        private static readonly MediaListEntry AttackOnTitanManga = new()
        {
            Id = 2001, MediaId = 53390, Status = MediaListStatus.Current, Progress = 100, Score = 9.0,
            Media = new Media
            {
                Id = 53390, Type = "MANGA", Format = "MANGA", Chapters = 141, Volumes = 34,
                AverageScore = 84, MeanScore = 85, Popularity = 213_000, Favourites = 32_400,
                IsFavourite = true, Trending = 42,
                Status = "FINISHED", Source = "ORIGINAL", CountryOfOrigin = "JP", IsLicensed = true,
                Title = new MediaTitle { Romaji = "Shingeki no Kyojin", English = "Attack on Titan", Native = "進撃の巨人" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx53390-1RsuABC34P9D.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx53390-1RsuABC34P9D.jpg",
                    Color = "#e4a15d",
                },
                BannerImage = "https://s4.anilist.co/file/anilistcdn/media/manga/banner/53390-6Uru5rrjh8zv.jpg",
                Description = "Hundreds of years ago, horrifying creatures which resembled humans appeared. These mindless, towering giants, called Titans, proved to be an existential threat, as they preyed on whatever humans they could find in order to satisfy a seemingly unending appetite.<br><br>The final remnants of humanity retreated behind the safety of enormous walls, and for a century, mankind lived in relative peace.",
                StartDate = new MediaDate { Year = 2009, Month = 9, Day = 9 },
                EndDate = new MediaDate { Year = 2021, Month = 4, Day = 9 },
                Synonyms = ["AoT", "SnK"],
                Genres = ["Action", "Drama", "Fantasy", "Mystery"],
                Tags =
                [
                    new MediaTag { Id = 1, Name = "Survival", Rank = 92, Category = "Theme-Other" },
                    new MediaTag { Id = 2, Name = "Military", Rank = 88, Category = "Theme-Other" },
                    new MediaTag { Id = 3, Name = "Tragedy", Rank = 85, Category = "Theme-Drama" },
                    new MediaTag { Id = 4, Name = "Male Protagonist", Rank = 80, Category = "Cast-Main Cast" },
                    // A spoiler tag so the details page's spoiler-reveal path is exercised too.
                    new MediaTag { Id = 5, Name = "Time Manipulation", Rank = 60, Category = "Theme-Sci Fi", IsMediaSpoiler = true },
                ],
                Rankings =
                [
                    new MediaRanking { Rank = 1, Type = "POPULAR", Format = "MANGA", AllTime = true, Context = "most popular all time" },
                    new MediaRanking { Rank = 12, Type = "RATED", Format = "MANGA", AllTime = true, Context = "highest rated all time" },
                    new MediaRanking { Rank = 3, Type = "RATED", Format = "MANGA", Year = 2021, AllTime = false, Context = "highest rated" },
                ],
                ExternalLinks =
                [
                    new MediaExternalLink { Id = 1, Site = "Official Site", Url = "https://shingeki.net/", Type = "INFO", Language = "Japanese", Color = "#3b3b3b" },
                    new MediaExternalLink { Id = 2, Site = "Kodansha", Url = "https://kodansha.us/series/attack-on-titan/", Type = "STREAMING", Language = "English", Color = "#c62828" },
                ],
                ScoreDistribution =
                [
                    new ScoreDistributionItem { Score = 10, Amount = 400 },
                    new ScoreDistributionItem { Score = 30, Amount = 900 },
                    new ScoreDistributionItem { Score = 50, Amount = 4_100 },
                    new ScoreDistributionItem { Score = 70, Amount = 15_800 },
                    new ScoreDistributionItem { Score = 90, Amount = 28_600 },
                    new ScoreDistributionItem { Score = 100, Amount = 11_200 },
                ],
                StatusDistribution =
                [
                    new StatusDistribution { Status = "CURRENT", Amount = 18_400 },
                    new StatusDistribution { Status = "PLANNING", Amount = 31_200 },
                    new StatusDistribution { Status = "COMPLETED", Amount = 74_900 },
                    new StatusDistribution { Status = "DROPPED", Amount = 3_100 },
                    new StatusDistribution { Status = "PAUSED", Amount = 6_700 },
                ],
                // Manga has no studios — AniList returns an empty edge list — so the Studios section
                // hiding itself on a manga page is part of what the screenshot should show.
                Characters =
                [
                    MangaCast(40882, "Eren Yeager", "https://s4.anilist.co/file/anilistcdn/character/large/b40882-dsj7IP943WFF.jpg"),
                    MangaCast(40881, "Mikasa Ackerman", "https://s4.anilist.co/file/anilistcdn/character/large/b40881-F3gr1PkreDvj.png"),
                    MangaCast(46494, "Armin Arlert", "https://s4.anilist.co/file/anilistcdn/character/large/b46494-g7xYYuBtYPnO.png"),
                    MangaCast(45627, "Levi", "https://s4.anilist.co/file/anilistcdn/character/large/b45627-CR68RyZmddGG.png"),
                ],
                // Manga staff roles read very differently from anime ones ("Story & Art" rather than
                // "Director"), which is worth seeing rendered.
                Staff =
                [
                    new StaffEdge
                    {
                        Role = "Story & Art",
                        Node = new StaffNode
                        {
                            Id = 106705,
                            Name = PersonName("Hajime Isayama", "諫山創"),
                            Image = new CharacterImage { Medium = "https://s4.anilist.co/file/anilistcdn/staff/medium/n106705-ttS2qZpF2FTZ.jpg" },
                            Favourites = 6_826,
                        },
                    },
                ],
                // The round trip back to the anime: #12's whole point is that this now navigates in
                // both directions instead of toasting in one of them.
                Relations =
                [
                    new MediaRelationEdge
                    {
                        RelationType = "ADAPTATION",
                        Node = new RelatedMedia
                        {
                            Id = 16498, Format = "TV", Type = "ANIME", Status = "FINISHED",
                            Episodes = 25, AverageScore = 85,
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin", English = "Attack on Titan" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx16498-buvcRTBx4NSm.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx16498-buvcRTBx4NSm.jpg",
                            },
                        },
                    },
                    new MediaRelationEdge
                    {
                        RelationType = "SPIN_OFF",
                        Node = new RelatedMedia
                        {
                            Id = 85199, Format = "MANGA", Type = "MANGA", Status = "FINISHED",
                            Chapters = 11, Volumes = 2,
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin Gaiden: Kuinaki Sentaku", English = "Attack on Titan: No Regrets" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx85199-IzuYa59zXTBN.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx85199-IzuYa59zXTBN.jpg",
                            },
                        },
                    },
                    // The one-shot is only reachable from here, and it is the only fixture that shows
                    // a Chapters chip with no Volumes chip beside it.
                    new MediaRelationEdge
                    {
                        RelationType = "SIDE_STORY",
                        Node = new RelatedMedia
                        {
                            Id = 85476, Format = "ONE_SHOT", Type = "MANGA", Status = "FINISHED",
                            Chapters = 1, Volumes = null, AverageScore = 72,
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin Gaiden: Kuinaki Sentaku - Prologue", English = "Attack on Titan: No Regrets - Prologue" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx85476-6DSMtBxcixMl.png",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx85476-6DSMtBxcixMl.png",
                            },
                        },
                    },
                ],
            },
        };

        /// <summary>
        /// Still-publishing manga, volume-tracked, reached from Luffy's character page. Chapters and
        /// volumes are both null — the shape AniList returns for EVERY releasing series — so there is
        /// no cap, no progress bar and no completion prompt, and the +1 button runs unbounded.
        /// Progress 0 with ProgressVolumes set is exactly the condition
        /// <see cref="MediaListEntry.UsesVolumeProgress"/> reads as "this reader tracks volumes".
        /// </summary>
        private static readonly MediaListEntry OnePieceManga = new()
        {
            Id = 2002, MediaId = 30013, Status = MediaListStatus.Current, Progress = 0, ProgressVolumes = 20, Score = 9.5,
            Media = new Media
            {
                Id = 30013, Type = "MANGA", Format = "MANGA", Chapters = null, Volumes = null,
                AverageScore = 91, MeanScore = 91, Popularity = 224_960, Favourites = 44_955,
                Status = "RELEASING", Source = "ORIGINAL", CountryOfOrigin = "JP",
                Title = new MediaTitle { Romaji = "ONE PIECE", English = "One Piece", Native = "ONE PIECE" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx30013-BeslEMqiPhlk.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx30013-BeslEMqiPhlk.jpg",
                    Color = "#f1935d",
                },
                Description = "Gol D. Roger, a man referred to as the King of the Pirates, is set to be executed by the World Government. But just before his demise, he confirms the existence of a great treasure, One Piece, located somewhere within the vast ocean known as the Grand Line.",
                StartDate = new MediaDate { Year = 1997, Month = 7, Day = 22 },
                Genres = ["Action", "Adventure", "Comedy", "Fantasy"],
                Relations =
                [
                    new MediaRelationEdge
                    {
                        RelationType = "ADAPTATION",
                        Node = new RelatedMedia
                        {
                            Id = 21, Format = "TV", Type = "ANIME", Status = "RELEASING", AverageScore = 87,
                            Title = new MediaTitle { Romaji = "ONE PIECE", English = "ONE PIECE" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21-ELSYx3yMPcKM.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx21-ELSYx3yMPcKM.jpg",
                            },
                        },
                    },
                ],
            },
        };

        /// <summary>
        /// Both counters set at once. Chapters win — the volume field is populated but not driving —
        /// which is the case that proves <see cref="MediaListEntry.UsesVolumeProgress"/> is not just
        /// "has volumes". Completed, so the details status picker shows a non-default selection.
        /// </summary>
        private static readonly MediaListEntry ChainsawManManga = new()
        {
            Id = 2003, MediaId = 105778, Status = MediaListStatus.Completed, Progress = 232, ProgressVolumes = 24, Score = 8.5,
            Media = new Media
            {
                Id = 105778, Type = "MANGA", Format = "MANGA", Chapters = 232, Volumes = 24,
                AverageScore = 85, MeanScore = 86, Popularity = 180_000, Favourites = 30_000,
                Status = "FINISHED", Source = "ORIGINAL", CountryOfOrigin = "JP",
                Title = new MediaTitle { Romaji = "Chainsaw Man", English = "Chainsaw Man", Native = "チェンソーマン" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx105778-euxXZEIfDY2u.png",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx105778-euxXZEIfDY2u.png",
                    Color = "#e4a15d",
                },
                Description = "Denji is robbed of a normal teenage life, left with nothing but his deadbeat father's debt. His only companion is his pet, the chainsaw devil Pochita, with whom he slays devils for money that inevitably ends up in the yakuza's pockets.",
                StartDate = new MediaDate { Year = 2018, Month = 12, Day = 3 },
                EndDate = new MediaDate { Year = 2020, Month = 12, Day = 14 },
                Genres = ["Action", "Comedy", "Horror", "Supernatural"],
            },
        };

        // ── Off-list manga ───────────────────────────────────────────────────────
        // No MediaListEntry at all, so GetMediaAsync returns media with a null entry — the shape the
        // details page needs for its "Add to List" affordance, which no other CI fixture produces.

        /// <summary>
        /// Attack on Titan's side story, and the second manga id sitting in the anime's relations
        /// carousel. Real id, title, counts and artwork — the fixture this replaced paired the No
        /// Regrets title with id 87459, which is a different series entirely, so its cover 404'd.
        /// </summary>
        private static readonly Media NoRegretsManga = new()
        {
            Id = 85199, Type = "MANGA", Format = "MANGA", Chapters = 11, Volumes = 2,
            AverageScore = 79, MeanScore = 79, Popularity = 16_202, Favourites = 482,
            Status = "FINISHED", Source = "VISUAL_NOVEL", CountryOfOrigin = "JP",
            Title = new MediaTitle { Romaji = "Shingeki no Kyojin Gaiden: Kuinaki Sentaku", English = "Attack on Titan: No Regrets", Native = "進撃の巨人 外伝 悔いなき選択" },
            CoverImage = new MediaCoverImage
            {
                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx85199-IzuYa59zXTBN.jpg",
                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx85199-IzuYa59zXTBN.jpg",
                Color = "#f16b5d",
            },
            Description = "A side story following Levi's life in the Underground City before he joined the Survey Corps.",
            StartDate = new MediaDate { Year = 2013, Month = 9, Day = 28 },
            EndDate = new MediaDate { Year = 2014, Month = 6, Day = 28 },
            Genres = ["Action", "Drama", "Fantasy"],
        };

        /// <summary>
        /// A one-shot: one chapter, no volume count. Two things only this shape shows — the progress
        /// popup's singular caption ("of 1 chapter"), and a manga whose Volumes chip is hidden while
        /// its Chapters chip is not. It is the prologue to <see cref="NoRegretsManga"/>, so it earns
        /// its place in the same relations carousel rather than arriving from nowhere.
        /// </summary>
        private static readonly Media NoRegretsPrologueOneShot = new()
        {
            Id = 85476, Type = "MANGA", Format = "ONE_SHOT", Chapters = 1, Volumes = null,
            AverageScore = 72, MeanScore = 74, Popularity = 2_940, Favourites = 38,
            Status = "FINISHED", CountryOfOrigin = "JP",
            Title = new MediaTitle { Romaji = "Shingeki no Kyojin Gaiden: Kuinaki Sentaku - Prologue", English = "Attack on Titan: No Regrets - Prologue", Native = "進撃の巨人 外伝 悔いなき選択 -プロローグ-" },
            CoverImage = new MediaCoverImage
            {
                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx85476-6DSMtBxcixMl.png",
                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx85476-6DSMtBxcixMl.png",
                Color = "#f19343",
            },
            Description = "A single-chapter prologue to the No Regrets side story.",
            StartDate = new MediaDate { Year = 2013, Month = 9, Day = 28 },
            EndDate = new MediaDate { Year = 2013, Month = 9, Day = 28 },
            Genres = ["Action", "Fantasy"],
        };

        /// <summary>
        /// A light novel. AniList files novels under type MANGA and separates them only by format, so
        /// this is what proves the details page keys off type rather than format — a NOVEL has to get
        /// the chapter/volume treatment, not the episode one.
        /// </summary>
        private static readonly Media MushokuTenseiNovel = new()
        {
            Id = 85470, Type = "MANGA", Format = "NOVEL", Chapters = 334, Volumes = 26,
            AverageScore = 85, MeanScore = 85, Popularity = 38_203, Favourites = 5_259,
            Status = "FINISHED", Source = "ORIGINAL", CountryOfOrigin = "JP",
            Title = new MediaTitle { Romaji = "Mushoku Tensei: Isekai Ittara Honki Dasu", English = "Mushoku Tensei: Jobless Reincarnation", Native = "無職転生 ～異世界行ったら本気だす～" },
            CoverImage = new MediaCoverImage
            {
                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/nx85470-jt6BF9tDWB2X.jpg",
                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/nx85470-jt6BF9tDWB2X.jpg",
                Color = "#f1bb1a",
            },
            Description = "A 34-year-old shut-in is hit by a truck and reincarnated into a world of swords and sorcery, determined not to waste a second life.",
            StartDate = new MediaDate { Year = 2014, Month = 1, Day = 23 },
            EndDate = new MediaDate { Year = 2022, Month = 11, Day = 25 },
            Genres = ["Adventure", "Drama", "Ecchi", "Fantasy"],
        };

        /// <summary>
        /// The manga-side 18+ canary, mirroring <see cref="AdultCanary"/> on the anime side. Manga
        /// search takes the same isAdult argument the anime search does, so without this the pin
        /// SearchPageModel keeps per result set would be unproven for half the app.
        /// </summary>
        private static readonly Media AdultMangaCanary = new()
        {
            Id = 999_002, Type = "MANGA", Format = "MANGA", Chapters = 12, Volumes = 1, IsAdult = true,
            Status = "FINISHED", CountryOfOrigin = "JP",
            Title = new MediaTitle { Romaji = AdultCanaryTitle, English = AdultCanaryTitle },
            // No CoverImage on purpose, as on the anime canary: ImageUrl.IsReal treats null as "no
            // image", so the app renders its own placeholder rather than fetching anything.
            Genres = ["Hentai"],
        };

        /// <summary>Manga the viewer has on their list, looked up by <c>GetMediaAsync</c>.</summary>
        public static readonly IReadOnlyList<MediaListEntry> MangaEntries =
            [AttackOnTitanManga, OnePieceManga, ChainsawManManga];

        /// <summary>
        /// The Library tab’s manga half (#12). Grouped under AniList’s own manga list names, which
        /// are Reading/Rereading rather than Watching/Rewatching — the whole point of giving the
        /// manga list its own section order.
        /// </summary>
        public static readonly IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> MangaGroupedList =
        [
            ("Reading",   [AttackOnTitanManga, OnePieceManga]),
            ("Completed", [ChainsawManManga]),
        ];

        /// <summary>
        /// Manga the viewer does NOT have on their list. <c>GetMediaAsync</c> falls back to these, so
        /// they resolve to a details page with no list entry.
        /// </summary>
        public static readonly IReadOnlyList<Media> OffListMedia =
            [NoRegretsManga, NoRegretsPrologueOneShot, MushokuTenseiNovel, AdultMangaCanary];

        public static readonly IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> GroupedList =
        [
            // The canary rides the Watching group so Library's client-side filter
            // (MediaListSectionsMerger.OrderAndFilterGroups) is exercised on every CI run.
            ("Watching",  [OnePiece, AttackOnTitan, AdultCanary, JujutsuKaisen, HunterXHunter]),
            ("Planning",  [YourName, PromisedNeverland]),
            ("Completed", [FmaB, DeathNote, ASilentVoice, DemonSlayer]),
        ];

        /// <summary>
        /// Discover/browse/search fixture: the stub list entries reshaped as browse items so every
        /// Discover section renders populated cards in CI screenshots. The order interleaves list
        /// statuses (and leaves some items off-list entirely) so the carousels and search results
        /// show a realistic mix of pills — Watching / Planning / Completed / none — rather than a
        /// run of identical chips. The explicit <c>status</c> override drives the pill independently
        /// of the entry's real status in <see cref="GroupedList"/>.
        /// </summary>
        public static readonly IReadOnlyList<BrowseMediaItem> BrowseItems =
        [
            BrowseItem(OnePiece, MediaListStatus.Current),
            BrowseItem(FmaB, MediaListStatus.Completed),
            BrowseItem(YourName, MediaListStatus.Planning),
            BrowseItem(JujutsuKaisen, status: null),          // not on list
            BrowseItem(AttackOnTitan, MediaListStatus.Current),
            BrowseItem(DemonSlayer, status: null),            // not on list
            BrowseItem(PromisedNeverland, MediaListStatus.Planning),
            BrowseItem(DeathNote, MediaListStatus.Completed),
            BrowseItem(HunterXHunter, status: null),          // not on list
            BrowseItem(ASilentVoice, MediaListStatus.Completed),
        ];

        /// <summary>
        /// The browse-side canary, kept out of <see cref="BrowseItems"/> so it can only ever be
        /// served by <see cref="BrowseItemsFor"/> deciding the filter allows it.
        /// <para>
        /// It lives in its own list rather than carrying a flag because <c>RelatedMedia</c> has no
        /// <c>IsAdult</c> — browse filtering is entirely server-side on AniList, so the client never
        /// receives the field. That is exactly why the stub has to model the filter itself: nothing
        /// on the client could re-derive it.
        /// </para>
        /// </summary>
        public static readonly IReadOnlyList<BrowseMediaItem> AdultCanaryBrowseItems =
        [
            BrowseItem(AdultCanary, status: null),
        ];

        /// <summary>
        /// Manga search results (#12). Separate from <see cref="BrowseItems"/> because Discover and
        /// View All are still anime-only — the real client pins <c>type: ANIME</c> in those queries,
        /// so a stub that mixed manga in would be modelling an endpoint that doesn't exist.
        /// <para>
        /// The mix of list statuses is deliberate, as on the anime side: two on-list entries in
        /// different states, two off-list, so the result rows show a realistic spread of status pills
        /// rather than a run of identical ones.
        /// </para>
        /// </summary>
        public static readonly IReadOnlyList<BrowseMediaItem> MangaBrowseItems =
        [
            BrowseItem(AttackOnTitanManga, MediaListStatus.Current),
            BrowseItem(OnePieceManga, MediaListStatus.Current),
            BrowseItem(ChainsawManManga, MediaListStatus.Completed),
            BrowseItem(NoRegretsManga, status: null),
            BrowseItem(NoRegretsPrologueOneShot, status: null),
            BrowseItem(MushokuTenseiNovel, status: null),
        ];

        /// <summary>The manga counterpart of <see cref="AdultCanaryBrowseItems"/>.</summary>
        public static readonly IReadOnlyList<BrowseMediaItem> AdultMangaCanaryBrowseItems =
        [
            BrowseItem(AdultMangaCanary, status: null),
        ];

        /// <summary>
        /// The manga half of <see cref="BrowseItemsFor"/>. Manga search takes the same
        /// <c>isAdult</c> argument the anime search does — modelling it only on one side would leave
        /// the per-result-set pin in <c>SearchPageModel</c> unproven for half the app.
        /// </summary>
        public static IReadOnlyList<BrowseMediaItem> MangaBrowseItemsFor(bool? isAdult) => isAdult switch
        {
            false => MangaBrowseItems,
            true => AdultMangaCanaryBrowseItems,
            null => [.. MangaBrowseItems, .. AdultMangaCanaryBrowseItems],
        };

        /// <summary>
        /// Applies the adult filter the way AniList's <c>isAdult</c> argument does, so the CI stub
        /// honours the same contract the real client relies on: <c>false</c> = SFW only,
        /// <c>true</c> = 18+ only, <c>null</c> = argument omitted, both mix in.
        /// </summary>
        public static IReadOnlyList<BrowseMediaItem> BrowseItemsFor(bool? isAdult) => isAdult switch
        {
            false => BrowseItems,
            true => AdultCanaryBrowseItems,
            null => [.. BrowseItems, .. AdultCanaryBrowseItems],
        };

        private static BrowseMediaItem BrowseItem(MediaListEntry entry, MediaListStatus? status)
            => BrowseItem(entry.Media!, status, entry.Id, entry.Progress, entry.Score);

        /// <summary>
        /// Overload for media with no list entry at all (#12) — the off-list manga fixtures. Passing
        /// a synthesised entry with Id 0 would work, but this makes "not on the user's list" the
        /// shape of the call rather than something the reader has to infer from the arguments.
        /// </summary>
        private static BrowseMediaItem BrowseItem(Media media, MediaListStatus? status)
            => BrowseItem(media, status, entryId: 0, progress: null, score: null);

        private static BrowseMediaItem BrowseItem(
            Media media, MediaListStatus? status, int entryId, int? progress, double? score)
        {
            var onList = status is not null;
            return new BrowseMediaItem
            {
                Node = new RelatedMedia
                {
                    Id = media.Id,
                    Title = media.Title,
                    Format = media.Format,
                    // Follows the fixture rather than being pinned to anime, so a manga search
                    // result opens as manga and its long-press flows read chapters (#12).
                    Type = media.Type ?? "ANIME",
                    Status = media.Status,
                    CoverImage = media.CoverImage,
                    AverageScore = media.AverageScore,
                    Favourites = media.Favourites,
                    Popularity = media.Popularity,
                    // Derived stub value so the Trending row's flame badge isn't all zeros in CI.
                    Trending = (media.Popularity ?? 0) / 250,
                    StartDate = media.StartDate,
                    Episodes = media.Episodes,
                    Chapters = media.Chapters,
                    Volumes = media.Volumes,
                    // Off-list items carry no entry id/status/progress/score — null everything, so
                    // the stub matches the real API (mediaListEntry absent → these are null) and no
                    // phantom score leaks into ToListEntry() if an add-to-list flow runs under stubs.
                    ListEntryId = onList ? entryId : null,
                    ListStatus = status,
                    ListProgress = onList ? progress : null,
                    ListScore = onList ? score : null,
                },
            };
        }

        // ── Character / Staff fixtures (Monkey D. Luffy + his JP seiyuu Mayumi Tanaka). ──────────────
        // Real AniList ids, images, scores, and roles, captured from the live API so the
        // character/staff detail screenshots show production-shaped lists. The page-1 sets are
        // marked complete (HasNextPage = false) so no Load More fires during capture.
        public static readonly Staff Staff = BuildStaffFixture();
        public static readonly Character Character = BuildCharacterFixture();
        public static readonly Studio Studio = BuildStudioFixture();

        // ---- Fixture builders ---------------------------------------------------------------------

        /// <summary>
        /// A fixture name carrying userPreferred, which is what names now render from (#130). The
        /// stubs cannot resolve it per-viewer the way AniList does, so it simply mirrors Full — enough
        /// for the CI capture to exercise the accessor rather than the fallback.
        /// </summary>
        private static CharacterName PersonName(string full, string? native = null)
            => new() { Full = full, Native = native, UserPreferred = full };

        private static CharacterEdge Cast(int id, string name, string image, int vaId, string vaName, string vaImage) => new()
        {
            Role = "MAIN",
            Node = new Character { Id = id, Name = PersonName(name), Image = new CharacterImage { Large = image, Medium = image } },
            VoiceActors = [Va(vaId, vaName, vaImage, "Japanese", null)],
        };

        /// <summary>
        /// A cast entry with no voice actor — manga characters have none, and AniList returns an
        /// empty voiceActors list for them (#12). Inventing one here would have put an anime VA on
        /// a manga page and, in the fixture that prompted this, four image URLs that 404.
        /// </summary>
        private static CharacterEdge MangaCast(int id, string name, string image) => new()
        {
            Role = "MAIN",
            Node = new Character { Id = id, Name = PersonName(name), Image = new CharacterImage { Large = image, Medium = image } },
            VoiceActors = [],
        };

        private static VoiceActor Va(int id, string name, string image, string language, int? favourites) => new()
        {
            Id = id,
            Name = PersonName(name),
            Image = new CharacterImage { Large = image, Medium = image },
            Language = language,
            Favourites = favourites,
        };

        private static CharacterMediaEdge Appearance(
            string role, int id, string format, string type, string status, int score, int popularity, int favourites,
            int year, string romaji, string english, string cover, string color, IReadOnlyList<VoiceActor>? voiceActors = null,
            MediaListStatus? listStatus = null) => new()
        {
            CharacterRole = role,
            Node = new RelatedMedia
            {
                Id = id, Format = format, Type = type, Status = status,
                AverageScore = score, Popularity = popularity, Favourites = favourites,
                StartDate = new MediaDate { Year = year },
                Title = new MediaTitle { Romaji = romaji, English = english },
                CoverImage = new MediaCoverImage { Large = cover, Medium = cover, Color = color },
                ListStatus = listStatus,
            },
            VoiceActors = voiceActors?.ToList() ?? [],
        };

        private static StaffCharacterEdge VoiceRole(
            string role, int charId, string charName, string charImage, int favourites,
            int mediaId, string mediaTitle, string mediaCover, string format) => new()
        {
            Role = role,
            Node = new Character
            {
                Id = charId, Favourites = favourites,
                Name = PersonName(charName),
                Image = new CharacterImage { Large = charImage, Medium = charImage },
            },
            Media = new RelatedMedia
            {
                Id = mediaId, Format = format, Type = "ANIME",
                Title = new MediaTitle { Romaji = mediaTitle, English = mediaTitle },
                CoverImage = new MediaCoverImage { Large = mediaCover, Medium = mediaCover },
            },
        };

        private static StaffMediaEdge ProductionRole(
            string role, int id, string format, string title, string cover, int year, int score,
            MediaListStatus? listStatus = null) => new()
        {
            StaffRole = role,
            Node = new RelatedMedia
            {
                Id = id, Format = format, Type = "ANIME", Status = "FINISHED", AverageScore = score,
                StartDate = new MediaDate { Year = year },
                Title = new MediaTitle { Romaji = title, English = title },
                CoverImage = new MediaCoverImage { Large = cover, Medium = cover },
                ListStatus = listStatus,
            },
        };

        private static StudioMediaEdge StudioProduction(
            int id, string format, string type, string status, int score, int popularity, int favourites,
            int year, string romaji, string english, string cover, string color,
            MediaListStatus? listStatus = null) => new()
        {
            Node = new RelatedMedia
            {
                Id = id, Format = format, Type = type, Status = status,
                AverageScore = score, Popularity = popularity, Favourites = favourites,
                StartDate = new MediaDate { Year = year },
                Title = new MediaTitle { Romaji = romaji, English = english },
                CoverImage = new MediaCoverImage { Large = cover, Medium = cover, Color = color },
                ListStatus = listStatus,
            },
        };

        private static Character BuildCharacterFixture()
        {
            // The voice-actor strip is built from the per-edge voiceActors below; the aggregator
            // dedups by staff id and groups by language (Japanese first), so this varied set yields
            // Mayumi Tanaka first, then the English / Italian / German dub actors.
            var onePieceVoiceActors = new List<VoiceActor>
            {
                Va(95075, "Mayumi Tanaka", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95075-1qD4TeW1ON92.png", "Japanese", 2418),
                Va(95472, "Colleen Clinkenbeard", "https://s4.anilist.co/file/anilistcdn/staff/medium/n95472-fznpewUW95vm.jpg", "English", 622),
                Va(101906, "Erica Schroeder", "https://s4.anilist.co/file/anilistcdn/staff/medium/n101906-Cul50rrR8cSA.png", "English", 20),
                Va(96163, "Renato Novara", "https://s4.anilist.co/file/anilistcdn/staff/medium/n96163-YaGjTo1XM8qd.png", "Italian", 56),
                Va(100143, "Daniel Schlauch", "https://s4.anilist.co/file/anilistcdn/staff/medium/n100143-47anXO76XYj9.png", "German", 60),
            };
            var tanakaOnly = new List<VoiceActor> { onePieceVoiceActors[0] };

            var character = new Character
            {
                Id = 40,
                Name = new CharacterName
                {
                    Full = "Monkey D. Luffy",
                    Native = "モンキー・D・ルフィ",
                    UserPreferred = "Monkey D. Luffy",
                    Alternative = ["Straw Hat", "Mugiwara no Luffy"],
                },
                Image = new CharacterImage
                {
                    Large = "https://s4.anilist.co/file/anilistcdn/character/large/b40-MNypXsxSRb1R.png",
                    Medium = "https://s4.anilist.co/file/anilistcdn/character/medium/b40-MNypXsxSRb1R.png",
                },
                Description = "Monkey D. Luffy is the founder and captain of the Straw Hat Pirates, and dreams of becoming the Pirate King by finding the legendary treasure, One Piece. After eating the Gum-Gum Fruit, his body gained the properties of rubber.",
                Gender = "Male",
                Age = "17-19",
                DateOfBirth = new MediaDate { Month = 5, Day = 5 },
                BloodType = "F",
                Favourites = 35_362,
                SiteUrl = "https://anilist.co/character/40",
                MediaPageInfo = new PageInfo { HasNextPage = false, CurrentPage = 1 },
            };

            foreach (var edge in new[]
            {
                Appearance("MAIN", 21, "TV", "ANIME", "RELEASING", 87, 708_195, 103_507, 1999, "ONE PIECE", "ONE PIECE", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21-ELSYx3yMPcKM.jpg", "#e49335", onePieceVoiceActors, MediaListStatus.Current),
                Appearance("MAIN", 30013, "MANGA", "MANGA", "RELEASING", 91, 224_960, 44_955, 1997, "ONE PIECE", "One Piece", "https://s4.anilist.co/file/anilistcdn/media/manga/cover/small/bx30013-BeslEMqiPhlk.jpg", "#f1935d"),
                Appearance("MAIN", 141902, "MOVIE", "ANIME", "FINISHED", 78, 74_600, 2_048, 2022, "ONE PIECE FILM: RED", "One Piece Film: Red", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx141902-fTyoTk8F8qOl.jpg", "#f1c950", tanakaOnly, MediaListStatus.Completed),
                Appearance("MAIN", 12859, "MOVIE", "ANIME", "FINISHED", 79, 62_142, 867, 2012, "ONE PIECE FILM: Z", "One Piece Film: Z", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx12859-uQFENDPzMWz6.jpg", "#f1ae5d", tanakaOnly, MediaListStatus.Completed),
                Appearance("MAIN", 105143, "MOVIE", "ANIME", "FINISHED", 80, 59_768, 1_228, 2019, "ONE PIECE STAMPEDE", "One Piece: Stampede", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx105143-5uBDmhvMr6At.png", "#e4e450", tanakaOnly, MediaListStatus.Planning),
                Appearance("MAIN", 21335, "MOVIE", "ANIME", "FINISHED", 77, 55_738, 704, 2016, "ONE PIECE FILM: GOLD", "One Piece Film: Gold", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/nx21335-XsXdE0AeOkkZ.jpg", "#f1bb35", tanakaOnly),
                Appearance("MAIN", 4155, "MOVIE", "ANIME", "FINISHED", 78, 53_829, 637, 2009, "ONE PIECE FILM: STRONG WORLD", "One Piece Film: Strong World", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx4155-P5TDf6t6qFwX.png", "#e4ae50", tanakaOnly),
                Appearance("SUPPORTING", 182469, "SPECIAL", "ANIME", "FINISHED", 90, 52_986, 3_032, 2024, "ONE PIECE FAN LETTER", "ONE PIECE FAN LETTER", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx182469-JQ808NBPxmgn.jpg", "#e4935d", tanakaOnly),
            })
            {
                character.Media.Add(edge);
            }

            return character;
        }

        private static Staff BuildStaffFixture()
        {
            var staff = new Staff
            {
                Id = 95075,
                Name = PersonName("Mayumi Tanaka", "田中真弓"),
                Image = new CharacterImage
                {
                    Large = "https://s4.anilist.co/file/anilistcdn/staff/large/n95075-1qD4TeW1ON92.png",
                    Medium = "https://s4.anilist.co/file/anilistcdn/staff/medium/n95075-1qD4TeW1ON92.png",
                },
                Description = "Mayumi Tanaka is a Japanese actress and voice actress from Tokyo, affiliated with Aoni Production. She is best known as the voice of Monkey D. Luffy in One Piece and Krillin in Dragon Ball.",
                LanguageV2 = "Japanese",
                PrimaryOccupations = ["Voice Actor"],
                Gender = "Female",
                DateOfBirth = new MediaDate { Year = 1955, Month = 1, Day = 15 },
                HomeTown = "Tokyo, Japan",
                YearsActive = [1978],
                Favourites = 2_418,
                SiteUrl = "https://anilist.co/staff/95075",
                StaffMediaPageInfo = new PageInfo { HasNextPage = false, CurrentPage = 1 },
                CharactersPageInfo = new PageInfo { HasNextPage = false, CurrentPage = 1 },
            };

            foreach (var role in new[]
            {
                VoiceRole("MAIN", 40, "Monkey D. Luffy", "https://s4.anilist.co/file/anilistcdn/character/large/b40-MNypXsxSRb1R.png", 35_362, 21, "ONE PIECE", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21-ELSYx3yMPcKM.jpg", "TV"),
                VoiceRole("MAIN", 1336, "Char Aznable", "https://s4.anilist.co/file/anilistcdn/character/large/b1336-VjllcTHMDuhI.png", 3_161, 10937, "Mobile Suit Gundam: The Origin", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx10937-yNrI4MUsigat.png", "OVA"),
                VoiceRole("MAIN", 2159, "Krillin", "https://s4.anilist.co/file/anilistcdn/character/large/b2159-qtEuMYyOUkwY.jpg", 958, 223, "Dragon Ball", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx223-scE5uJfXqqj8.png", "TV"),
                VoiceRole("SUPPORTING", 239956, "Turbo Granny", "https://s4.anilist.co/file/anilistcdn/character/large/b239956-Fok0Pl3rNOEL.png", 835, 171018, "Dan Da Dan", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx171018-60q1B6GK2Ghb.jpg", "TV"),
                VoiceRole("SUPPORTING", 2305, "Koenma", "https://s4.anilist.co/file/anilistcdn/character/large/2305.jpg", 268, 392, "Yu Yu Hakusho", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx392-z90299zIvYmx.png", "TV"),
                VoiceRole("MAIN", 8524, "Pazu", "https://s4.anilist.co/file/anilistcdn/character/large/b8524-GsNaG6GxiZrP.jpg", 253, 513, "Castle in the Sky", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx513-yM7Dlt65N4Rl.jpg", "MOVIE"),
                VoiceRole("SUPPORTING", 2097, "Yajirobe", "https://s4.anilist.co/file/anilistcdn/character/large/2097.jpg", 100, 223, "Dragon Ball", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx223-scE5uJfXqqj8.png", "TV"),
                VoiceRole("MAIN", 19095, "Giovanni", "https://s4.anilist.co/file/anilistcdn/character/large/b19095-GB6OYl2A5EuH.png", 38, 1441, "Night on the Galactic Railroad", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/b1441-NOIDPZ2svpoS.jpg", "MOVIE"),
            })
            {
                staff.Characters.Add(role);
            }

            foreach (var role in new[]
            {
                ProductionRole("Theme Song Performance (OP, ED2)", 1165, "OVA", "Sakura Wars: The Gorgeous Blooming Cherry Blossoms", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/b1165-cmxTudQc5wHO.jpg", 1997, 64, MediaListStatus.Completed),
                ProductionRole("Theme Song Performance (ED)", 4150, "OVA", "Cosmos Pink Shock", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/nx4150-ab0QGFDZIn68.jpg", 1986, 52),
                ProductionRole("Theme Song Performance (ED)", 15913, "TV", "Happy Lucky Bikkuriman", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/15913.jpg", 2006, 58),
                ProductionRole("Insert Song Performance", 16253, "MOVIE", "Umi da! Funade da! Nikoniko, Pun", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx16253-ckp6jCg44OHf.png", 1990, 54),
            })
            {
                staff.StaffMedia.Add(role);
            }

            return staff;
        }

        private static Studio BuildStudioFixture()
        {
            var studio = new Studio
            {
                Id = 18,
                Name = "Toei Animation",
                IsAnimationStudio = true,
                Favourites = 8_730,
                SiteUrl = "https://anilist.co/studio/18",
                MediaPageInfo = new PageInfo { HasNextPage = false, CurrentPage = 1 },
            };

            foreach (var production in new[]
            {
                StudioProduction(21, "TV", "ANIME", "RELEASING", 87, 708_195, 103_507, 1999, "ONE PIECE", "ONE PIECE", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21-ELSYx3yMPcKM.jpg", "#e49335", MediaListStatus.Current),
                StudioProduction(223, "TV", "ANIME", "FINISHED", 78, 387_724, 9_173, 1986, "Dragon Ball", "Dragon Ball", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx223-scE5uJfXqqj8.png", "#f1bb35", MediaListStatus.Completed),
                StudioProduction(813, "TV", "ANIME", "FINISHED", 82, 420_892, 11_246, 1989, "Dragon Ball Z", "Dragon Ball Z", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx813-vG7I3BTL9H3G.jpg", "#f1ae35", MediaListStatus.Completed),
                StudioProduction(141902, "MOVIE", "ANIME", "FINISHED", 78, 74_600, 2_048, 2022, "ONE PIECE FILM: RED", "One Piece Film: Red", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx141902-fTyoTk8F8qOl.jpg", "#f1c950"),
                StudioProduction(12859, "MOVIE", "ANIME", "FINISHED", 79, 62_142, 867, 2012, "ONE PIECE FILM: Z", "One Piece Film: Z", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx12859-uQFENDPzMWz6.jpg", "#f1ae5d"),
                StudioProduction(101001, "MOVIE", "ANIME", "FINISHED", 82, 114_793, 2_522, 2018, "Dragon Ball Super: Broly", "Dragon Ball Super: Broly", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx101001-N4Dy57wKQf0g.jpg", "#0da1e4", MediaListStatus.Planning),
            })
            {
                studio.Media.Add(production);
            }

            return studio;
        }
    }
}
#endif
