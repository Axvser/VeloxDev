using System.Drawing;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// 自绘控件的 OnPaint/OnPaintBackground 可以在无窗口进程里用 DrawToBitmap 触发。
[STATestClass]
public sealed class RenderingHeadlessTests
{
    [TestMethod]
    public void GridDecorator_PaintsWithoutAWindow()
    {
        using var decorator = new WorkflowGridDecorator { Width = 200, Height = 100 };
        decorator.CreateControl();

        using var bitmap = new Bitmap(200, 100);
        decorator.DrawToBitmap(bitmap, new Rectangle(0, 0, 200, 100));

        Assert.AreEqual(200, bitmap.Width);
    }

    [TestMethod]
    public void Minimap_PaintsWithoutAWindow()
    {
        var graph = TestGraph.Create();
        using var minimap = new WorkflowMinimapOverlay
        {
            Width = 200,
            Height = 140,
            WorkflowTree = graph.Tree,
        };
        minimap.CreateControl();

        using var bitmap = new Bitmap(200, 140);
        minimap.DrawToBitmap(bitmap, new Rectangle(0, 0, 200, 140));

        Assert.AreEqual(200, bitmap.Width);
    }

    [TestMethod]
    public void SurfaceColors_ParseArgbAndRgb()
    {
        Assert.AreEqual(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), WorkflowSurfaceColors.Parse("#FF102030"));
        Assert.AreEqual(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), WorkflowSurfaceColors.Parse("#102030"));
    }
}
