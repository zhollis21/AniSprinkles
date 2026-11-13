using AniSprinkles.Models;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.UnitTests;

// Regression: Appears In / Production Roles re-stamp MetricBadge on the *same* edge objects during an
// in-memory (local) sort change. Without change notification, recycled CollectionView cells kept the
// previous sort's badge — producing the mixed/stale badges seen under "Most Watched". These edges must
// raise PropertyChanged for both MetricBadge and the derived HasMetricBadge.
public class MetricBadgeEdgeTests
{
    private static ItemMetricBadge Badge() =>
        new() { Glyph = "x", IconColor = Colors.White, Text = "1" };

    [Test]
    public void CharacterMediaEdge_SettingMetricBadge_RaisesPropertyChanged()
    {
        var edge = new CharacterMediaEdge();
        var changed = new List<string?>();
        edge.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        edge.MetricBadge = Badge();

        Assert.That(changed, Does.Contain(nameof(CharacterMediaEdge.MetricBadge)));
        Assert.That(changed, Does.Contain(nameof(CharacterMediaEdge.HasMetricBadge)));
        Assert.That(edge.HasMetricBadge, Is.True);
    }

    [Test]
    public void StaffMediaEdge_SettingMetricBadge_RaisesPropertyChanged()
    {
        var edge = new StaffMediaEdge();
        var changed = new List<string?>();
        edge.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        edge.MetricBadge = Badge();

        Assert.That(changed, Does.Contain(nameof(StaffMediaEdge.MetricBadge)));
        Assert.That(changed, Does.Contain(nameof(StaffMediaEdge.HasMetricBadge)));
        Assert.That(edge.HasMetricBadge, Is.True);
    }
}
