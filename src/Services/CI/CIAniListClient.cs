#if CI
namespace AniSprinkles.Services;

/// <summary>
/// CI-only stub that returns hardcoded anime data so screenshot builds show a fully
/// authenticated, populated UI without needing a real AniList OAuth token.
/// Compiled out of Debug and Release builds entirely — only active when -p:CiBuild=true.
/// </summary>
internal sealed class CIAniListClient : IAniListClient
{
    public Task<AniListUser> GetViewerAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(StubData.Viewer);

    public Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>> GetMyAnimeListGroupedAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(StubData.GroupedList);

    public Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MediaListEntry> flat = StubData.GroupedList
            .SelectMany(g => g.Entries)
            .ToList();
        return Task.FromResult(flat);
    }

    public Task<(Media? Media, MediaListEntry? ListEntry)> GetMediaAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var entry = StubData.GroupedList
            .SelectMany(g => g.Entries)
            .FirstOrDefault(e => e.MediaId == id);
        return Task.FromResult((entry?.Media, entry));
    }

    public Task<IReadOnlyList<Media>> SearchAnimeAsync(
        string search, int page = 1, int perPage = 20, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Media>>([]);

    public Task<MediaListEntry?> SaveMediaListEntryAsync(
        MediaListEntry entry, CancellationToken cancellationToken = default)
        => Task.FromResult<MediaListEntry?>(entry);

    public Task<bool> DeleteMediaListEntryAsync(int entryId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(StubData.Viewer.Id);

    public Task<AniListUser> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(StubData.Viewer);

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
                Studios =
                [
                    new Studio { Id = 18, Name = "Toei Animation", IsAnimationStudio = true, IsMain = true },
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
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx53390-1RsuABC34P9D.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/large/bx53390-1RsuABC34P9D.jpg",
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
                            Id = 87459, Format = "NOVEL", Type = "MANGA",
                            Title = new MediaTitle { Romaji = "Shingeki no Kyojin: Kuinaki Sentaku", English = "Attack on Titan: No Regrets" },
                            CoverImage = new MediaCoverImage
                            {
                                Medium = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/87459-GlbVHMPqVkHG.jpg",
                                Large = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/large/87459-GlbVHMPqVkHG.jpg",
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

        public static readonly IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> GroupedList =
        [
            ("Watching",  [OnePiece, AttackOnTitan, JujutsuKaisen, HunterXHunter]),
            ("Planning",  [YourName, PromisedNeverland]),
            ("Completed", [FmaB, DeathNote, ASilentVoice, DemonSlayer]),
        ];

        // ── Character / Staff fixtures (Monkey D. Luffy + his JP seiyuu Mayumi Tanaka). ──────────────
        // Real AniList ids, images, scores, and roles, captured from the live API so the
        // character/staff detail screenshots show production-shaped lists. The page-1 sets are
        // marked complete (HasNextPage = false) so no Load More fires during capture.
        public static readonly Staff Staff = BuildStaffFixture();
        public static readonly Character Character = BuildCharacterFixture();
        public static readonly Studio Studio = BuildStudioFixture();

        // ---- Fixture builders ---------------------------------------------------------------------

        private static CharacterEdge Cast(int id, string name, string image, int vaId, string vaName, string vaImage) => new()
        {
            Role = "MAIN",
            Node = new Character { Id = id, Name = new CharacterName { Full = name }, Image = new CharacterImage { Large = image, Medium = image } },
            VoiceActors = [Va(vaId, vaName, vaImage, "Japanese", null)],
        };

        private static VoiceActor Va(int id, string name, string image, string language, int? favourites) => new()
        {
            Id = id,
            Name = new CharacterName { Full = name },
            Image = new CharacterImage { Large = image, Medium = image },
            Language = language,
            Favourites = favourites,
        };

        private static CharacterMediaEdge Appearance(
            string role, int id, string format, string type, string status, int score, int popularity, int favourites,
            int year, string romaji, string english, string cover, string color, IReadOnlyList<VoiceActor>? voiceActors = null) => new()
        {
            CharacterRole = role,
            Node = new RelatedMedia
            {
                Id = id, Format = format, Type = type, Status = status,
                AverageScore = score, Popularity = popularity, Favourites = favourites,
                StartDate = new MediaDate { Year = year },
                Title = new MediaTitle { Romaji = romaji, English = english },
                CoverImage = new MediaCoverImage { Large = cover, Medium = cover, Color = color },
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
                Name = new CharacterName { Full = charName },
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
            string role, int id, string format, string title, string cover, int year, int score) => new()
        {
            StaffRole = role,
            Node = new RelatedMedia
            {
                Id = id, Format = format, Type = "ANIME", Status = "FINISHED", AverageScore = score,
                StartDate = new MediaDate { Year = year },
                Title = new MediaTitle { Romaji = title, English = title },
                CoverImage = new MediaCoverImage { Large = cover, Medium = cover },
            },
        };

        private static StudioMediaEdge StudioProduction(
            int id, string format, string type, string status, int score, int popularity, int favourites,
            int year, string romaji, string english, string cover, string color) => new()
        {
            Node = new RelatedMedia
            {
                Id = id, Format = format, Type = type, Status = status,
                AverageScore = score, Popularity = popularity, Favourites = favourites,
                StartDate = new MediaDate { Year = year },
                Title = new MediaTitle { Romaji = romaji, English = english },
                CoverImage = new MediaCoverImage { Large = cover, Medium = cover, Color = color },
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
                Appearance("MAIN", 21, "TV", "ANIME", "RELEASING", 87, 708_195, 103_507, 1999, "ONE PIECE", "ONE PIECE", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21-ELSYx3yMPcKM.jpg", "#e49335", onePieceVoiceActors),
                Appearance("MAIN", 30013, "MANGA", "MANGA", "RELEASING", 91, 224_960, 44_955, 1997, "ONE PIECE", "One Piece", "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/bx30013-BeslEMqiPhlk.jpg", "#f1935d"),
                Appearance("MAIN", 141902, "MOVIE", "ANIME", "FINISHED", 78, 74_600, 2_048, 2022, "ONE PIECE FILM: RED", "One Piece Film: Red", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx141902-fTyoTk8F8qOl.jpg", "#f1c950", tanakaOnly),
                Appearance("MAIN", 12859, "MOVIE", "ANIME", "FINISHED", 79, 62_142, 867, 2012, "ONE PIECE FILM: Z", "One Piece Film: Z", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx12859-uQFENDPzMWz6.jpg", "#f1ae5d", tanakaOnly),
                Appearance("MAIN", 105143, "MOVIE", "ANIME", "FINISHED", 80, 59_768, 1_228, 2019, "ONE PIECE STAMPEDE", "One Piece: Stampede", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx105143-5uBDmhvMr6At.png", "#e4e450", tanakaOnly),
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
                Name = new CharacterName { Full = "Mayumi Tanaka", Native = "田中真弓", UserPreferred = "Mayumi Tanaka" },
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
                ProductionRole("Theme Song Performance (OP, ED2)", 1165, "OVA", "Sakura Wars: The Gorgeous Blooming Cherry Blossoms", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/b1165-cmxTudQc5wHO.jpg", 1997, 64),
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
                StudioProduction(21, "TV", "ANIME", "RELEASING", 87, 708_195, 103_507, 1999, "ONE PIECE", "ONE PIECE", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx21-ELSYx3yMPcKM.jpg", "#e49335"),
                StudioProduction(223, "TV", "ANIME", "FINISHED", 78, 387_724, 9_173, 1986, "Dragon Ball", "Dragon Ball", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx223-scE5uJfXqqj8.png", "#f1bb35"),
                StudioProduction(813, "TV", "ANIME", "FINISHED", 82, 420_892, 11_246, 1989, "Dragon Ball Z", "Dragon Ball Z", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx813-vG7I3BTL9H3G.jpg", "#f1ae35"),
                StudioProduction(141902, "MOVIE", "ANIME", "FINISHED", 78, 74_600, 2_048, 2022, "ONE PIECE FILM: RED", "One Piece Film: Red", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx141902-fTyoTk8F8qOl.jpg", "#f1c950"),
                StudioProduction(12859, "MOVIE", "ANIME", "FINISHED", 79, 62_142, 867, 2012, "ONE PIECE FILM: Z", "One Piece Film: Z", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx12859-uQFENDPzMWz6.jpg", "#f1ae5d"),
                StudioProduction(101001, "MOVIE", "ANIME", "FINISHED", 82, 114_793, 2_522, 2018, "Dragon Ball Super: Broly", "Dragon Ball Super: Broly", "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx101001-N4Dy57wKQf0g.jpg", "#0da1e4"),
            })
            {
                studio.Media.Add(production);
            }

            return studio;
        }
    }
}
#endif
