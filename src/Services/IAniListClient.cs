using AniSprinkles.Models;

namespace AniSprinkles.Services
{
    public interface IAniListClient
    {
        Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Media>> SearchAnimeAsync(string search, int page = 1, int perPage = 20, CancellationToken cancellationToken = default);
        Task<Media?> GetMediaAsync(int id, CancellationToken cancellationToken = default);
        Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default);
    }
}
