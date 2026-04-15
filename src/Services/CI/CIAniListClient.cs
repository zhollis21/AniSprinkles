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
                    new Studio { Id = 18, Name = "Toei Animation", IsAnimationStudio = true },
                ],
                // Airs today in 3 hours — exercises the short countdown airing path
                NextAiringEpisode = MakeAiringEpisode(1160, DateTimeOffset.UtcNow.AddHours(3)),
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
    }
}
#endif
