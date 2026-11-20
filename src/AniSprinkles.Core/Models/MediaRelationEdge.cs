using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public class MediaRelationEdge : ObservableObject, IDisplayProjection
{
    public string? RelationType { get; set; }

    /// <summary>The same relation as <see cref="RelationType"/>, typed, for code that branches on it (#103).</summary>
    public MediaRelationKind Kind { get; set; }
    public RelatedMedia? Node { get; set; }

    // The card's metric badge (year), stamped by the PageModel; always shown (— when no year).
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Re-raising <c>Node</c> is what makes the nested <c>Node.DisplayTitle</c> binding re-resolve;
    /// <c>RelatedMedia</c> is a plain class and cannot notify for itself.
    /// </remarks>
    public void RefreshDisplayProjections() => OnPropertyChanged(nameof(Node));
}
