using System.Drawing;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>The visual <see cref="VC.VirtualList"/> only rebuilds the realized rows when the visible range changes.</summary>
[STATestClass]
public sealed class VirtualizationTests
{
    [TestMethod]
    public void ScrollingWithinOneRow_DoesNotRebuildTheRealizedItems()
    {
        var rows = Enumerable.Range(0, 400).Select(static index => (object)$"Row {index + 1}").ToList();
        using var viewer = new VC.ScrollViewer { Width = 200, Height = 100 };
        using var list = new VC.VirtualList { ItemHeight = 28, ItemsSource = rows };
        viewer.Content = list;

        Layout(viewer, 200, 100);

        Assert.AreEqual(0, list.FirstRealizedIndex, "the first visible row is realized");
        Assert.IsGreaterThan(0, list.RealizedCount);
        Assert.IsLessThan(rows.Count, list.RealizedCount, "only the visible rows are realized");
        var realizedCount = list.RealizedCount;

        viewer.VerticalOffset = 10;
        Layout(viewer, 200, 100);

        Assert.AreEqual(0, list.FirstRealizedIndex, "a sub-row offset only moves the host");
        Assert.AreEqual(realizedCount, list.RealizedCount);

        viewer.VerticalOffset = 60;
        Layout(viewer, 200, 100);

        Assert.IsGreaterThan(0, list.FirstRealizedIndex, "crossing a row boundary rebuilds the visible set");
    }

    [TestMethod]
    public void TheVirtualListReportsTheFullExtentToTheScrollViewer()
    {
        var rows = Enumerable.Range(0, 100).Select(static index => (object)index).ToList();
        using var viewer = new VC.ScrollViewer { Width = 200, Height = 100 };
        using var list = new VC.VirtualList { ItemHeight = 20, ItemsSource = rows };
        viewer.Content = list;

        Layout(viewer, 200, 100);

        Assert.AreEqual(2000f, viewer.ExtentHeight, 0.1f);
        Assert.AreEqual(100f, viewer.ViewportHeight, 0.1f);
        Assert.AreEqual(1900f, viewer.MaxVerticalOffset, 0.1f);
    }

    private static void Layout(VisualElement root, int width, int height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new RectangleF(0f, 0f, width, height));
    }
}
