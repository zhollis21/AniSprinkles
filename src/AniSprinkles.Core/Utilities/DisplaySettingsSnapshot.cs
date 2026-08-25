namespace AniSprinkles.Utilities;

/// <summary>
/// The display settings a surface's current rendering was built under, captured so the surface can
/// notice on its next appearance that one of them moved (#127).
/// <para>
/// This is #118's compare-on-appearing shape applied to the settings that change <em>how</em>
/// already-fetched items render rather than <em>which</em> items exist. The distinction matters:
/// adult content is applied while a result set is built, so only a refetch can honour a change,
/// whereas title language and score format feed computed properties over data already in hand. Those
/// want a re-projection, and spending an AniList request on a formatting change would be the wrong
/// trade under the rate-limit budget.
/// </para>
/// <para>
/// A comparison rather than a change event on <c>AppSettings</c>: <c>MediaBrowsePageModel</c> and the
/// four details page models are registered transient but stay alive in their tab's navigation stack,
/// so a static event would need subscribing and unsubscribing on every appear/disappear to avoid
/// retaining every page model the user ever navigated to. There is nothing to leak here, and every
/// page already has an <c>OnAppearing</c> hook.
/// </para>
/// </summary>
/// <param name="TitleLanguage">Drives <c>Media.DisplayTitle</c> and <c>RelatedMedia.DisplayTitle</c>.</param>
/// <param name="ScoreFormat">Drives <c>MediaListEntry.ScoreDisplay</c> and the details rating control.</param>
/// <param name="AnimeSectionOrder">
/// Joined rather than held as a list so the record's value equality actually compares the contents.
/// A <c>List&lt;string&gt;</c> member would compare by reference and never report a change.
/// </param>
public readonly record struct DisplaySettingsSnapshot(
    UserTitleLanguage TitleLanguage,
    ScoreFormat ScoreFormat,
    string AnimeSectionOrder)
{
    /// <summary>The settings as they stand right now.</summary>
    public static DisplaySettingsSnapshot Current => new(
        AppSettings.TitleLanguage,
        AppSettings.ScoreFormat,
        string.Join(",", AppSettings.AnimeSectionOrder));

    /// <summary>
    /// True when something that affects how already-fetched items render has moved. Section order is
    /// deliberately excluded — it reorders sections rather than re-rendering their contents, and only
    /// Library has sections to reorder.
    /// </summary>
    public bool RenderingDiffersFrom(DisplaySettingsSnapshot other)
        => TitleLanguage != other.TitleLanguage || ScoreFormat != other.ScoreFormat;

    /// <summary>
    /// True when the Title sort would now order entries differently. <c>MediaListSection</c> sorts by
    /// <c>Media.DisplayTitle</c>, so a language change moves rows as well as re-rendering them.
    /// </summary>
    public bool TitleLanguageDiffersFrom(DisplaySettingsSnapshot other)
        => TitleLanguage != other.TitleLanguage;

    public bool SectionOrderDiffersFrom(DisplaySettingsSnapshot other)
        => !string.Equals(AnimeSectionOrder, other.AnimeSectionOrder, StringComparison.Ordinal);
}
