using System.Collections.ObjectModel;
using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;

namespace AniSprinkles.UnitTests;

/// <summary>
/// Fluent helpers for building MediaListEntry graphs used across merger tests.
/// Values are kept intentionally minimal — tests that need specific Media fields
/// overwrite them via the optional parameters.
/// </summary>
internal static class TestDataBuilder
{
    public static MediaListEntry Entry(
        int mediaId,
        string? title = null,
        string? englishTitle = null,
        string? coverMedium = null,
        int? progress = null,
        double? score = null,
        MediaListStatus? status = MediaListStatus.Current,
        bool isAdult = false,
        DateTimeOffset? updatedAt = null,
        int? episodes = null,
        int? nextAiringEpisode = null,
        int? nextAiringAt = null,
        string? mediaType = null,
        int? chapters = null,
        int? volumes = null,
        int? progressVolumes = null,
        string? mediaStatus = null)
    {
        return new MediaListEntry
        {
            Id = mediaId * 10,
            MediaId = mediaId,
            Status = status,
            Progress = progress,
            ProgressVolumes = progressVolumes,
            Score = score,
            UpdatedAt = updatedAt,
            Media = new Media
            {
                Id = mediaId,
                Title = new MediaTitle
                {
                    Romaji = title ?? $"Title-{mediaId}",
                    English = englishTitle,
                },
                CoverImage = new MediaCoverImage { Medium = coverMedium ?? $"https://img/{mediaId}" },
                IsAdult = isAdult,
                Type = mediaType,
                Status = mediaStatus,
                Episodes = episodes,
                Chapters = chapters,
                Volumes = volumes,
                NextAiringEpisode = nextAiringEpisode is null
                    ? null
                    : new MediaAiringEpisode { Episode = nextAiringEpisode, AiringAt = nextAiringAt },
            },
        };
    }

    public static (string Name, IReadOnlyList<MediaListEntry> Entries) Group(
        string name, params MediaListEntry[] entries) => (name, entries);

    public static IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> Groups(
        params (string, IReadOnlyList<MediaListEntry>)[] groups) => groups;

    /// <summary>
    /// Primes an ObservableCollection{MediaListSection} by running the cold path shape
    /// (OrderAndFilterGroups + per-section AddItems + ApplySort) so warm-path tests start
    /// with a realistic populated state.
    /// </summary>
    public static ObservableCollection<MediaListSection> BuildInitial(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups,
        IReadOnlyList<string>? sectionOrder = null,
        bool displayAdult = true,
        SortField sortField = SortField.LastUpdated,
        bool sortAscending = false,
        string filterText = "")
    {
        var sections = new ObservableCollection<MediaListSection>();
        var ordered = MediaListSectionsMerger.OrderAndFilterGroups(groups, sectionOrder ?? [], displayAdult);

        foreach (var group in ordered)
        {
            var defaultExpanded = sections.Count == 0 || group.Name == "Rewatching";
            var section = new MediaListSection(group.Name, defaultExpanded);
            section.AddItems(group.Entries);
            section.ApplySort(sortField, sortAscending);
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                section.ApplyFilter(filterText);
            }

            sections.Add(section);
        }

        return sections;
    }

    /// <summary>
    /// Resets AppSettings to known defaults before a test so shared static state doesn't bleed
    /// across tests, and swaps in a fresh <see cref="FakePreferences"/> so the persistence paths
    /// (Load/Save/Clear/SyncFromViewer) are reachable — the real <c>Preferences.Default</c> throws
    /// NotImplementedInReferenceAssemblyException on this TFM (#121).
    /// <para>
    /// The properties are still set directly rather than through Clear(): the point is a known
    /// starting state, and going through the storage path would make every test depend on Clear()
    /// being correct. Test classes that call this belong in the <see cref="AppSettingsCollection"/>
    /// so they don't race each other over these statics — including <c>AppSettings.Storage</c>.
    /// </para>
    /// </summary>
    /// <returns>The fake now installed, for tests that want to assert on what was persisted.</returns>
    public static FakePreferences ResetAppSettings()
    {
        var preferences = new FakePreferences();
        AppSettings.Storage = preferences;

        // Clear() resets the pending-upstream markers as well as the values (#128). Those markers
        // are process-wide statics that nothing else here can reach, so without this a test that
        // changed a setting locally leaves one set, and the NEXT test's SyncFromViewer keeps the
        // stale local value instead of taking the server's — a failure that depends entirely on
        // ordering within the shared collection, so it reproduces on CI and not always locally.
        // Assigned directly below rather than through the Set* methods, which would re-arm them.
        AppSettings.Clear();

        AppSettings.TitleLanguage = UserTitleLanguage.Romaji;
        AppSettings.ScoreFormat = ScoreFormat.Point100;
        AppSettings.DisplayAdultContent = true;
        AppSettings.AnimeSectionOrder = [];

        return preferences;
    }
}
