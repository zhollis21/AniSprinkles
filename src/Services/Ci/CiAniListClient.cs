#if CI
namespace AniSprinkles.Services;

/// <summary>
/// CI-only stub that returns hardcoded anime data so screenshot builds show a fully
/// authenticated, populated UI without needing a real AniList OAuth token.
/// Compiled out of Debug and Release builds entirely — only active when -p:CiBuild=true.
/// </summary>
internal sealed class CiAniListClient : IAniListClient
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

    // ---------------------------------------------------------------------------
    // Stub data — built once, shared across all method calls
    // ---------------------------------------------------------------------------

    private static class StubData
    {
        public static readonly AniListUser Viewer = new()
        {
            Id = 999999,
            Name = "CiUser",
            AvatarLarge = "https://s4.anilist.co/file/anilistcdn/user/avatar/large/default.png",
            ScoreFormat = ScoreFormat.Point10Decimal,
            AnimeSectionOrder = ["Current", "Planning", "Completed", "Dropped", "Paused", "Repeating"],
            Options = new UserOptions
            {
                TitleLanguage = UserTitleLanguage.Romaji,
                AiringNotifications = true,
                ProfileColor = "blue",
            },
            AnimeStatistics = new UserAnimeStatistics
            {
                Count = 127,
                EpisodesWatched = 1840,
                MinutesWatched = 46000,
                MeanScore = 7.4,
            },
        };

        // Watching entries
        private static readonly MediaListEntry Frieren = new()
        {
            Id = 1001, MediaId = 154587, Status = MediaListStatus.Current, Progress = 20,
            Media = new Media
            {
                Id = 154587, Format = "TV", Episodes = 28, AverageScore = 91,
                Status = "RELEASING", Season = "FALL", SeasonYear = 2023,
                Title = new MediaTitle { Romaji = "Sousou no Frieren", English = "Frieren: Beyond Journey's End" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx154587-nhs0nksPLlk7.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx154587-nhs0nksPLlk7.jpg",
                    Color = "#e47850",
                },
                Genres = ["Adventure", "Drama", "Fantasy"],
                // Episode 21 airs in ~3 days — exercises the "Ep N in Xd" airing info path
                NextAiringEpisode = new MediaAiringEpisode
                {
                    Episode = 21,
                    AiringAt = (int)DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds(),
                },
            },
        };

        private static readonly MediaListEntry DungeonMeshi = new()
        {
            Id = 1002, MediaId = 163132, Status = MediaListStatus.Current, Progress = 19,
            Media = new Media
            {
                Id = 163132, Format = "TV", Episodes = 24, AverageScore = 87,
                Status = "RELEASING", Season = "WINTER", SeasonYear = 2024,
                Title = new MediaTitle { Romaji = "Dungeon Meshi", English = "Delicious in Dungeon" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx163132-D0HuqZ1jRmXJ.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx163132-D0HuqZ1jRmXJ.jpg",
                    Color = "#e4a150",
                },
                Genres = ["Adventure", "Comedy", "Fantasy"],
            },
        };

        private static readonly MediaListEntry Hyouka = new()
        {
            Id = 1003, MediaId = 13701, Status = MediaListStatus.Current, Progress = 12,
            Media = new Media
            {
                Id = 13701, Format = "TV", Episodes = 22, AverageScore = 83,
                Status = "FINISHED", Season = "SPRING", SeasonYear = 2012,
                Title = new MediaTitle { Romaji = "Hyouka", English = "Hyouka" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx13701-SBFQheGqFrm1.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx13701-SBFQheGqFrm1.jpg",
                    Color = "#8db4d4",
                },
                Genres = ["Mystery", "Romance", "Slice of Life"],
            },
        };

        private static readonly MediaListEntry VinlandSaga = new()
        {
            Id = 1004, MediaId = 101348, Status = MediaListStatus.Current, Progress = 18,
            Media = new Media
            {
                Id = 101348, Format = "TV", Episodes = 24, AverageScore = 85,
                Status = "FINISHED", Season = "SUMMER", SeasonYear = 2019,
                Title = new MediaTitle { Romaji = "Vinland Saga", English = "Vinland Saga" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx101348-6cIVSzdnSmZX.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx101348-6cIVSzdnSmZX.jpg",
                    Color = "#3d6887",
                },
                Genres = ["Action", "Adventure", "Drama"],
            },
        };

        // Completed entries
        private static readonly MediaListEntry FmaB = new()
        {
            Id = 1005, MediaId = 5114, Status = MediaListStatus.Completed, Progress = 64, Score = 9.5,
            Media = new Media
            {
                Id = 5114, Format = "TV", Episodes = 64, AverageScore = 92,
                Status = "FINISHED", Season = "SPRING", SeasonYear = 2009,
                Title = new MediaTitle { Romaji = "Fullmetal Alchemist: Brotherhood", English = "Fullmetal Alchemist: Brotherhood" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx5114-6fvPGBkxEjMj.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx5114-6fvPGBkxEjMj.jpg",
                    Color = "#e45150",
                },
                Genres = ["Action", "Adventure", "Drama", "Fantasy"],
            },
        };

        private static readonly MediaListEntry SteinsGate = new()
        {
            Id = 1006, MediaId = 9253, Status = MediaListStatus.Completed, Progress = 24, Score = 9.0,
            Media = new Media
            {
                Id = 9253, Format = "TV", Episodes = 24, AverageScore = 91,
                Status = "FINISHED", Season = "SPRING", SeasonYear = 2011,
                Title = new MediaTitle { Romaji = "Steins;Gate", English = "Steins;Gate" },
                CoverImage = new MediaCoverImage
                {
                    Medium = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/medium/bx9253-uHhrBs0uyxhF.jpg",
                    Large = "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx9253-uHhrBs0uyxhF.jpg",
                    Color = "#3464a4",
                },
                Genres = ["Drama", "Sci-Fi", "Thriller"],
            },
        };

        public static readonly IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> GroupedList =
        [
            ("Current", (IReadOnlyList<MediaListEntry>)[Frieren, DungeonMeshi, Hyouka, VinlandSaga]),
            ("Completed", (IReadOnlyList<MediaListEntry>)[FmaB, SteinsGate]),
        ];
    }
}
#endif
