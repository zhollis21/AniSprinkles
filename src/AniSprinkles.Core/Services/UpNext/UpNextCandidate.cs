using System.Text;
using AniSprinkles.Utilities;

namespace AniSprinkles.Services;

/// <summary>
/// One related title the user could act on after completing something (#103): either one to add
/// (<see cref="Existing"/> null) or one already on the list to move (<see cref="Existing"/> is the
/// viewer's whole entry for it, which the move saves back). Made only by
/// <see cref="UpNextCandidates.Select"/>, which decides <see cref="Target"/> once, from the rules and
/// the Settings list, so a row's label, its save and its toast can never disagree about where it goes.
/// </summary>
public sealed record UpNextCandidate
{
    internal UpNextCandidate(RelatedMedia media, MediaRelationKind relation, MediaListEntry? existing, MediaListStatus target)
    {
        Media = media;
        Relation = relation;
        Existing = existing;
        From = existing?.Status;
        Target = target;
    }

    public RelatedMedia Media { get; }

    public MediaRelationKind Relation { get; }

    /// <summary>The viewer's whole entry for a title already on the list; null for a new one.</summary>
    public MediaListEntry? Existing { get; }

    /// <summary>
    /// Where an existing entry sat when it was offered. Captured rather than read from
    /// <see cref="Existing"/>, which the move itself rewrites.
    /// </summary>
    public MediaListStatus? From { get; }

    /// <summary>The status this title is saved under if the user picks it.</summary>
    public MediaListStatus Target { get; }

    /// <summary>AniList's <c>NOT_YET_RELEASED</c>.</summary>
    public bool IsUnreleased => UpNextCandidates.IsUnreleased(Media);

    /// <summary>Not on the list yet, so picking it adds it.</summary>
    public bool IsNew => Existing is null;

    public string RelationLabel => Relation switch
    {
        MediaRelationKind.Sequel => "Sequel",
        MediaRelationKind.SideStory => "Side story",
        MediaRelationKind.SpinOff => "Spin-off",
        MediaRelationKind.Alternative => "Alternative",
        _ => string.Empty,
    };

    /// <summary>"TV · 2026": format and year for the row's details chip, absent parts omitted.</summary>
    public string DetailsLine
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var part in (ReadOnlySpan<string>)[Media.FormatDisplay, Media.YearDisplay])
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" · ");
                }

                sb.Append(part);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Where this row goes, for its destination chip: "→ Watching" for a new title, "Completed →
    /// Rewatching" for a move. Each row says its own, which is why the sheet has no list switch: rows
    /// can go to different lists.
    /// </summary>
    public string DestinationLabel => From is { } from
        ? $"{MediaListVocabulary.StatusLabel(from, Media.Kind)} → {MediaListVocabulary.StatusLabel(Target, Media.Kind)}"
        : $"→ {MediaListVocabulary.StatusLabel(Target, Media.Kind)}";
}

/// <summary>
/// The rules for which relations of a just-completed entry are worth offering, and where each goes
/// (#103). Pure, so every rule is pinned by a test rather than by a device run.
/// </summary>
public static class UpNextCandidates
{
    // Offered relation types, in the order they are listed. Everything else (prequel, parent, source,
    // adaptation, character, summary and compilation recaps, contains, other, same universe) is not a
    // "what comes next".
    private static readonly MediaRelationKind[] Offered =
    [
        MediaRelationKind.Sequel,
        MediaRelationKind.SideStory,
        MediaRelationKind.SpinOff,
        MediaRelationKind.Alternative,
    ];

    /// <summary>
    /// Eligible relations of a <paramref name="kind"/> entry: an offered relation type, the same media
    /// type, not hidden by the 18+ setting, and somewhere <see cref="TargetFor"/> has a move for.
    /// Sequels first, then by release date, so the list reads in watching order.
    /// </summary>
    /// <param name="settingsStatus">
    /// The Settings list, Watching or Planning: where titles go unless a rule below says otherwise.
    /// </param>
    public static IReadOnlyList<UpNextCandidate> Select(
        MediaKind kind,
        IEnumerable<MediaRelationEdge> edges,
        bool displayAdultContent,
        MediaListStatus settingsStatus)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (settingsStatus is not (MediaListStatus.Current or MediaListStatus.Planning))
        {
            throw new ArgumentOutOfRangeException(nameof(settingsStatus), settingsStatus, "Related titles go to Watching or Planning.");
        }

        var seen = new HashSet<int>();
        var eligible = new List<UpNextCandidate>();
        foreach (var edge in edges)
        {
            if (edge.Node is { Id: > 0 } node
                && Array.IndexOf(Offered, edge.Kind) >= 0
                && node.Kind == kind
                && !AdultMediaFilter.Hides(node, displayAdultContent)
                && TargetFor(node, settingsStatus) is { } target
                && seen.Add(node.Id))
            {
                eligible.Add(new UpNextCandidate(node, edge.Kind, node.ViewerEntry, target));
            }
        }

        return eligible
            .OrderBy(c => Array.IndexOf(Offered, c.Relation))
            .ThenBy(c => c.Media.StartDate?.Year ?? int.MaxValue)
            .ThenBy(c => c.Media.StartDate?.Month ?? int.MaxValue)
            .ThenBy(c => c.Media.StartDate?.Day ?? int.MaxValue)
            .ThenBy(c => c.Media.Id)
            .ToList();
    }

    /// <summary>
    /// The sheet's confirm button for a selection: "Add 2", "Move 1", "Add 2 · Move 1", or
    /// "Select titles" when nothing is ticked (the button is disabled then, and says why).
    /// </summary>
    public static string ConfirmLabel(IReadOnlyCollection<UpNextCandidate> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        var adds = selected.Count(c => c.IsNew);
        var moves = selected.Count - adds;
        return (adds, moves) switch
        {
            (0, 0) => "Select titles",
            (_, 0) => $"Add {adds}",
            (0, _) => $"Move {moves}",
            _ => $"Add {adds} · Move {moves}",
        };
    }

    internal static bool IsUnreleased(RelatedMedia media)
        => string.Equals(media.Status, "NOT_YET_RELEASED", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where a related title goes, given where it sits on the user's list; null when it is not worth
    /// offering. Everything follows <paramref name="settingsStatus"/> except:
    /// <list type="bullet">
    /// <item>an unreleased title only ever goes to Planning, wherever it sits (it cannot be watched
    /// yet);</item>
    /// <item>a cancelled title is never offered: one that never aired has nothing to watch, and the
    /// relations query cannot tell it from one cancelled mid-run;</item>
    /// <item>a Completed title goes to Rewatching rather than Watching;</item>
    /// <item>Watching and Rewatching titles are already in progress, and a title already in its target
    /// list has nowhere to move, so neither is offered.</item>
    /// </list>
    /// A title that is on the list without its whole entry (<see cref="RelatedMedia.ViewerEntry"/>) is
    /// never offered: moving it would save the missing fields as nulls.
    /// </summary>
    private static MediaListStatus? TargetFor(RelatedMedia node, MediaListStatus settingsStatus)
    {
        if (string.Equals(node.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var startTo = IsUnreleased(node) ? MediaListStatus.Planning : settingsStatus;
        if (node.ViewerEntry is not { } entry)
        {
            return node.ListStatus is null && node.ListEntryId is null ? startTo : null;
        }

        var target = entry.Status switch
        {
            MediaListStatus.Planning or MediaListStatus.Paused or MediaListStatus.Dropped => startTo,
            MediaListStatus.Completed => startTo == MediaListStatus.Current ? MediaListStatus.Repeating : startTo,
            _ => (MediaListStatus?)null,
        };
        return target == entry.Status ? null : target;
    }
}
