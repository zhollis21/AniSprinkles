using AniSprinkles.Models;

namespace AniSprinkles.Services
{
    public class MockAniListClient : IAniListClient
    {
        private readonly List<MediaListEntry> _entries;

        public MockAniListClient()
        {
            _entries = BuildSampleList();
        }

        public Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MediaListEntry>>(_entries);

        public Task<IReadOnlyList<Media>> SearchAnimeAsync(string search, int page = 1, int perPage = 20, CancellationToken cancellationToken = default)
        {
            var results = _entries
                .Select(e => e.Media)
                .Where(m => m is not null)
                .Where(m => m!.DisplayTitle.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Cast<Media>()
                .ToList();

            return Task.FromResult<IReadOnlyList<Media>>(results);
        }

        public Task<Media?> GetMediaAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.FirstOrDefault(e => e.MediaId == id)?.Media);

        public Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default)
        {
            var existing = _entries.FirstOrDefault(e => e.MediaId == entry.MediaId);
            if (existing is null)
            {
                entry.Id = _entries.Max(e => e.Id) + 1;
                _entries.Add(entry);
                return Task.FromResult<MediaListEntry?>(entry);
            }

            existing.Status = entry.Status;
            existing.Progress = entry.Progress;
            existing.Score = entry.Score;
            existing.Repeat = entry.Repeat;
            existing.Notes = entry.Notes;
            existing.Private = entry.Private;
            existing.HiddenFromStatusLists = entry.HiddenFromStatusLists;
            existing.UpdatedAt = DateTimeOffset.Now;

            return Task.FromResult<MediaListEntry?>(existing);
        }

        private static List<MediaListEntry> BuildSampleList()
        {
            return
            [
                new MediaListEntry
                {
                    Id = 1,
                    MediaId = 154587,
                    Status = MediaListStatus.Current,
                    Progress = 20,
                    Score = 9.5,
                    UpdatedAt = DateTimeOffset.Now.AddDays(-1),
                    Media = new Media
                    {
                        Id = 154587,
                        Title = new MediaTitle
                        {
                            English = "Frieren: Beyond Journey's End",
                            Romaji = "Sousou no Frieren",
                            Native = "葬送のフリーレン"
                        },
                        Format = "TV",
                        Episodes = 28,
                        Season = "FALL",
                        SeasonYear = 2023
                    }
                },
                new MediaListEntry
                {
                    Id = 2,
                    MediaId = 5114,
                    Status = MediaListStatus.Completed,
                    Progress = 64,
                    Score = 10,
                    UpdatedAt = DateTimeOffset.Now.AddDays(-30),
                    Media = new Media
                    {
                        Id = 5114,
                        Title = new MediaTitle
                        {
                            English = "Fullmetal Alchemist: Brotherhood",
                            Romaji = "Hagane no Renkinjutsushi: Fullmetal Alchemist",
                            Native = "鋼の錬金術師 FULLMETAL ALCHEMIST"
                        },
                        Format = "TV",
                        Episodes = 64,
                        Season = "SPRING",
                        SeasonYear = 2009
                    }
                },
                new MediaListEntry
                {
                    Id = 3,
                    MediaId = 145064,
                    Status = MediaListStatus.Completed,
                    Progress = 23,
                    Score = 8.5,
                    UpdatedAt = DateTimeOffset.Now.AddDays(-10),
                    Media = new Media
                    {
                        Id = 145064,
                        Title = new MediaTitle
                        {
                            English = "Jujutsu Kaisen Season 2",
                            Romaji = "Jujutsu Kaisen 2nd Season",
                            Native = "呪術廻戦 第2期"
                        },
                        Format = "TV",
                        Episodes = 23,
                        Season = "SUMMER",
                        SeasonYear = 2023
                    }
                },
                new MediaListEntry
                {
                    Id = 4,
                    MediaId = 101348,
                    Status = MediaListStatus.Completed,
                    Progress = 24,
                    Score = 9,
                    UpdatedAt = DateTimeOffset.Now.AddDays(-60),
                    Media = new Media
                    {
                        Id = 101348,
                        Title = new MediaTitle
                        {
                            English = "Vinland Saga",
                            Romaji = "Vinland Saga",
                            Native = "ヴィンランド・サガ"
                        },
                        Format = "TV",
                        Episodes = 24,
                        Season = "SUMMER",
                        SeasonYear = 2019
                    }
                },
                new MediaListEntry
                {
                    Id = 5,
                    MediaId = 127230,
                    Status = MediaListStatus.Planning,
                    Progress = 0,
                    Score = null,
                    UpdatedAt = DateTimeOffset.Now.AddDays(-5),
                    Media = new Media
                    {
                        Id = 127230,
                        Title = new MediaTitle
                        {
                            English = "Chainsaw Man",
                            Romaji = "Chainsaw Man",
                            Native = "チェンソーマン"
                        },
                        Format = "TV",
                        Episodes = 12,
                        Season = "FALL",
                        SeasonYear = 2022
                    }
                }
            ];
        }
    }
}
