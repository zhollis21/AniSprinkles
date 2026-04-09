namespace AniSprinkles.Services;

public interface IAniListClient
{
    Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>> GetMyAnimeListGroupedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Media>> SearchAnimeAsync(string search, int page = 1, int perPage = 20, CancellationToken cancellationToken = default);
    Task<(Media? Media, MediaListEntry? ListEntry)> GetMediaAsync(int id, CancellationToken cancellationToken = default);
    Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default);
    Task<bool> DeleteMediaListEntryAsync(int entryId, CancellationToken cancellationToken = default);
    Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default);
    Task<AniListUser> GetViewerAsync(CancellationToken cancellationToken = default);
    Task<AniListUser> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiringScheduleEntry>> GetAiringScheduleAsync(IReadOnlyList<int> mediaIds, int airingAfter, int airingBefore, CancellationToken cancellationToken = default);
}
