using System.Drawing;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>Virtualized scrolling only rebuilds when the visible row range changes.</summary>
[STATestClass]
public sealed class VirtualizationTests
{
    [TestMethod]
    public void ScrollingWithinOneRow_DoesNotRelayoutTheRealizedItems()
    {
        var rows = Enumerable.Range(0, 400).Select(static index => (object)$"Row {index + 1}").ToList();
        using var viewer = new V.ScrollViewer
        {
            Size = new Size(200, 100),
            ExtentHeight = 400 * 28,
            VerticalScrollBarVisibility = V.ScrollBarVisibility.Visible,
        };
        using var canvas = new V.Canvas();
        using var list = new CountingItemsControl
        {
            Width = 200,
            Height = 400 * 28,
            Virtualizing = true,
            ItemHeight = 28,
            ItemsSource = rows,
        };
        canvas.Controls.Add(list);
        viewer.Controls.Add(canvas);
        viewer.PerformLayout();
        list.PerformLayout();

        var baseline = list.Layouts;

        viewer.ScrollOffset = new Point(0, 10);
        Assert.AreEqual(baseline, list.Layouts, "a sub-row offset change only moves the host");

        viewer.ScrollOffset = new Point(0, 60);
        Assert.IsGreaterThan(baseline, list.Layouts, "crossing a row boundary rebuilds the visible set");
    }

    private sealed class CountingItemsControl : V.ItemsControl
    {
        public int Layouts { get; private set; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            Layouts++;
            base.OnLayout(e);
        }
    }
}
