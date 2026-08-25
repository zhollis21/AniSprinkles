using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

/// <summary>
/// Tracks the title language a surface's cards were rendered under, and re-projects them when it
/// moves (#127).
/// <para>
/// Deliberately narrower than <see cref="DisplaySettingsSnapshot"/>: the browse and carousel surfaces
/// show a community average score (<c>RelatedMedia.ScoreDisplay</c>, computed from
/// <c>AverageScore</c>) rather than the viewer's own, so the score-format setting does not reach
/// them. Only the title does. Library and the Media Details rating control are the exceptions and
/// compare the full snapshot themselves.
/// </para>
/// </summary>
public sealed class TitleProjectionWatcher
{
    private DisplaySettingsSnapshot _rendered = DisplaySettingsSnapshot.Current;

    /// <summary>
    /// Re-raises title projections on <paramref name="items"/> if the title language has moved since
    /// the last call, then records the current settings either way.
    /// </summary>
    /// <remarks>
    /// Called from the surface's appearing path <em>before</em> any freshness short-circuit, since the
    /// case this exists for is precisely the one where no load runs.
    /// </remarks>
    public void RefreshIfTitleLanguageChanged(IEnumerable<IDisplayProjection> items)
    {
        var current = DisplaySettingsSnapshot.Current;

        if (current.TitleLanguageDiffersFrom(_rendered))
        {
            foreach (var item in items)
            {
                item.RefreshDisplayProjections();
            }
        }

        _rendered = current;
    }

    /// <summary>
    /// Records that what is on screen now matches the current settings — after a load has rebuilt it,
    /// so the next appearance does not redo work the rebuild already did.
    /// </summary>
    public void MarkRendered() => _rendered = DisplaySettingsSnapshot.Current;
}
