namespace AniSprinkles.Models;

/// <summary>
/// An item whose bound text is computed from <c>AppSettings</c> and therefore goes stale when a
/// display setting changes underneath it (#127).
/// <para>
/// The awkward part this exists to paper over: <c>Media</c> and <c>RelatedMedia</c> carry the
/// computed <c>DisplayTitle</c> but are plain classes, so they cannot raise <c>PropertyChanged</c>
/// themselves. Implementers are the observable <em>containers</em> that hold one, and they re-raise
/// the property carrying it — MAUI then re-resolves the whole binding path, <c>DisplayTitle</c>
/// included, without those POCOs needing change notification of their own.
/// </para>
/// </summary>
public interface IDisplayProjection
{
    /// <summary>
    /// Re-raises change notification for every bound member computed from a display setting. Does no
    /// work beyond notification — the underlying data is unchanged and no fetch is involved.
    /// </summary>
    void RefreshDisplayProjections();
}
