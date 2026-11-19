using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WPF.Headless.Tests.Tests;

// 小地图是自绘控件：量好、排好、用 RenderTargetBitmap 走一次 OnRender，
// 就能在无窗口进程里覆盖绘制分支（不需要真实呈现源）。
[STATestClass]
public sealed class MinimapHeadlessTests
{
    [TestMethod]
    public void Minimap_RendersNodesAndViewport()
    {
        var graph = TestGraph.Create();
        var minimap = new WorkflowMinimapOverlay
        {
            Width = 200d,
            Height = 140d,
            WorkflowTree = graph.Tree,
            ViewportWidth = 800d,
            ViewportHeight = 600d,
            IsMinimapVisible = true,
        };

        minimap.Measure(new System.Windows.Size(200d, 140d));
        minimap.Arrange(new Rect(0d, 0d, 200d, 140d));

        var bitmap = new RenderTargetBitmap(200, 140, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(minimap);

        Assert.AreEqual(200, bitmap.PixelWidth);
        Assert.AreEqual(140, bitmap.PixelHeight);
    }

    [TestMethod]
    public void Minimap_Hidden_StillMeasures()
    {
        var graph = TestGraph.Create();
        var minimap = new WorkflowMinimapOverlay
        {
            Width = 200d,
            Height = 140d,
            WorkflowTree = graph.Tree,
            IsMinimapVisible = false,
        };

        minimap.Measure(new System.Windows.Size(200d, 140d));
        minimap.Arrange(new Rect(0d, 0d, 200d, 140d));

        var bitmap = new RenderTargetBitmap(200, 140, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(minimap);

        Assert.AreEqual(200, bitmap.PixelWidth);
    }
}
