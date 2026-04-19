using System.Collections.ObjectModel;
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
        string? coverMedium = null,
        int? progress = null,
        double? score = null,
        MediaListStatus? status = MediaListStatus.Current,
        bool isAdult = false,
        DateTimeOffset? updatedAt = null,
        int? episodes = null,
        int? nextAiringEpisode = null,
        int? nextAiringAt = null)
    {
        return new MediaListEntry
        {
            Id = mediaId * 10,
            MediaId = mediaId,
            Status = status,
            Progress = progress,
            Score = score,
            UpdatedAt = updatedAt,
            Media = new Media
            {
                Id = mediaId,
                Title = new MediaTitle { Romaji = title ?? $"Title-{mediaId}" },
                CoverImage = new MediaCoverImage { Medium = coverMedium ?? $"https://img/{mediaId}" },
                IsAdult = isAdult,
                Episodes = episodes,
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
    /// across tests. AppSettings.Load()/Save()/Clear() require MAUI Preferences and are not
    /// linked into the test project.
    /// </summary>
    public static void ResetAppSettings()
    {
        AppSettings.TitleLanguage = UserTitleLanguage.Romaji;
        AppSettings.ScoreFormat = ScoreFormat.Point100;
        AppSettings.DisplayAdultContent = true;
        AppSettings.AnimeSectionOrder = [];
    }
}
