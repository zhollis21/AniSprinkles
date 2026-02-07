using AniSprinkles.Models;

namespace AniSprinkles.Services
{
    public class AniListClient : IAniListClient
    {
        public Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException("AniList API client not implemented yet.");

        public Task<IReadOnlyList<Media>> SearchAnimeAsync(string search, int page = 1, int perPage = 20, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("AniList API client not implemented yet.");

        public Task<Media?> GetMediaAsync(int id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("AniList API client not implemented yet.");

        public Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("AniList API client not implemented yet.");
    }
}
