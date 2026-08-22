using System.Collections.ObjectModel;
using AniSprinkles.Models;

namespace AniSprinkles.PageModels;

/// <summary>
/// Builds the deduped, language-grouped voice-actor list for the character page.
///
/// AniList exposes voice actors only nested inside a character's media edges (there is no direct
/// "voice actors of a character" query), and the same seiyuu recurs across appearances — so the
/// list is deduped by staff id and ordered Japanese-first, then by language, then by favourites.
/// Crucially this aggregator walks its OWN fixed (popularity) media cursor, completely independent
/// of the "Appears In" section's sort, so changing that sort never disturbs the voice-actor list.
///
/// Because most extra media pages repeat already-seen seiyuu, <see cref="CheckForMoreAsync"/> walks
/// forward up to a capped number of pages per call until it surfaces at least one new voice actor
/// (or exhausts the media), which is why the UI affordance is "check for more" rather than a plain
/// "load more". MAUI-free for unit testing.
/// </summary>
public sealed class VoiceActorAggregator
{
    public delegate Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> FetchMediaPageDelegate(
        int page, CancellationToken cancellationToken);

    private readonly FetchMediaPageDelegate _fetchPage;
    private readonly int _maxPagesPerCheck;

    private readonly HashSet<int> _seenVoiceActorIds = [];
    private int _currentPage;
    private int _generation;

    public VoiceActorAggregator(FetchMediaPageDelegate fetchPage, int maxPagesPerCheck = 3)
    {
        _fetchPage = fetchPage;
        _maxPagesPerCheck = Math.Max(1, maxPagesPerCheck);
    }

    public ObservableCollection<VoiceActor> Items { get; } = [];

    /// <summary>True while the character has more media pages that could surface new voice actors.</summary>
    public bool HasMore { get; private set; }

    public bool IsChecking { get; private set; }

    public bool IsEmpty => Items.Count == 0;

    public event Action? Changed;

    /// <summary>Seeds from the first media page returned by the heavy Character query.</summary>
    public void Seed(IReadOnlyList<CharacterMediaEdge> mediaEdges, PageInfo? pageInfo)
    {
        _generation++;
        Items.Clear();
        _seenVoiceActorIds.Clear();
        IsChecking = false;
        AddFromEdges(mediaEdges);
        _currentPage = pageInfo?.CurrentPage ?? 1;
        HasMore = pageInfo?.HasNextPage ?? false;
        Notify();
    }

    /// <summary>
    /// Walks forward (up to the per-call page cap) until at least one new voice actor is found or
    /// the media is exhausted. No-op while already checking or fully paged.
    /// </summary>
    public async Task CheckForMoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsChecking || !HasMore)
        {
            return;
        }

        IsChecking = true;
        Notify();

        var generation = _generation;
        try
        {
            var pagesWalked = 0;
            while (HasMore && pagesWalked < _maxPagesPerCheck)
            {
                var nextPage = _currentPage + 1;
                var (edges, pageInfo) = await _fetchPage(nextPage, cancellationToken).ConfigureAwait(true);
                if (generation != _generation)
                {
                    return; // reset/new character superseded this walk
                }

                pagesWalked++;
                _currentPage = nextPage;
                HasMore = pageInfo?.HasNextPage ?? false;

                if (AddFromEdges(edges) > 0)
                {
                    break; // surfaced new seiyuu this tap
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Page torn down mid-walk — drop silently.
        }
        finally
        {
            if (generation == _generation)
            {
                IsChecking = false;
                Notify();
            }
        }
    }

    public void Reset()
    {
        _generation++;
        Items.Clear();
        _seenVoiceActorIds.Clear();
        _currentPage = 0;
        HasMore = false;
        IsChecking = false;
        Notify();
    }

    private int AddFromEdges(IReadOnlyList<CharacterMediaEdge> edges)
    {
        var added = 0;
        foreach (var edge in edges)
        {
            foreach (var va in edge.VoiceActors)
            {
                if (_seenVoiceActorIds.Add(va.Id))
                {
                    InsertSorted(va);
                    added++;
                }
            }
        }

        return added;
    }

    private void InsertSorted(VoiceActor va)
    {
        // Small list (a handful per character) — linear insert keeps existing order/scroll stable.
        for (var i = 0; i < Items.Count; i++)
        {
            if (Compare(va, Items[i]) < 0)
            {
                Items.Insert(i, va);
                return;
            }
        }

        Items.Add(va);
    }

    private static int Compare(VoiceActor a, VoiceActor b)
    {
        var rank = LanguageRank(a.Language).CompareTo(LanguageRank(b.Language));
        if (rank != 0)
        {
            return rank;
        }

        var lang = string.Compare(a.Language ?? string.Empty, b.Language ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        if (lang != 0)
        {
            return lang;
        }

        var favourites = (b.Favourites ?? 0).CompareTo(a.Favourites ?? 0); // most-favorited first
        if (favourites != 0)
        {
            return favourites;
        }

        return string.Compare(a.Name?.Full ?? string.Empty, b.Name?.Full ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    // Japanese (the original seiyuu) sorts ahead of every dub language.
    private static int LanguageRank(string? language) =>
        string.Equals(language, "Japanese", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private void Notify() => Changed?.Invoke();
}
