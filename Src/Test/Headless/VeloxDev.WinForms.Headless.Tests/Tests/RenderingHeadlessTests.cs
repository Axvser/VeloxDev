using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WinForms.Headless.Tests.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using XamlRuntime = VeloxDev.WinForms.Xaml.XamlRuntime;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// 视觉后端不需要窗口：VisualHost.RenderTo 直接把整棵视觉树画进位图，OnRender 在这里被触发。
[STATestClass]
public sealed class RenderingHeadlessTests
{
    [TestMethod]
    public void Surface_SettlesLayoutBetweenFrames()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph, withMinimap: true);
        var field = typeof(VisualHost).GetField("_layoutValid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        for (var pass = 0; pass < 4; pass++)
        {
            surface.Render();
        }

        Assert.IsTrue((bool)field.GetValue(surface.View)!, "a stable surface keeps no pending layout");
    }

    [TestMethod]
    public void VisualGridDecorator_RendersWithoutAWindow()
    {
        var decorator = new TemplateGridDecorator
        {
            Width = 200,
            Height = 100,
            ScrollOffsetX = -40,
        };

        using var host = new VisualHost { Size = new DrawingSize(200, 100), DpiScaleOverride = 1f };
        host.Root = decorator;

        using var bitmap = new Bitmap(200, 100);
        using var graphics = Graphics.FromImage(bitmap);
        host.RenderTo(graphics, new DrawingSize(200, 100));

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
        using var host = new VisualHost { Size = new DrawingSize(200, 140), DpiScaleOverride = 1f };
        host.Root = minimap;
        using var bitmap = new Bitmap(200, 140);
        using var graphics = Graphics.FromImage(bitmap);
        host.RenderTo(graphics, new DrawingSize(200, 140));

        Assert.AreEqual(200, bitmap.Width);
    }

    [TestMethod]
    public void Minimap_ReceivesTheCameraViewportFromTheSurface()
    {
        var graph = TestGraph.Create();
        using var surface = new WinFormsHeadlessSurface(graph, withMinimap: true);
        var minimap = surface.Minimap!;

        WorkflowSurfaceBehavior.SetWorldViewport(surface.View, new Offset(60d, 40d));
        surface.Render();

        Assert.AreSame(graph.Tree, minimap.WorkflowTree);
        Assert.AreEqual(60d, minimap.ScrollOffsetX, 1d);
        Assert.AreEqual(40d, minimap.ScrollOffsetY, 1d);
        // 纵向/横向条各占 8px 后，presenter 的内容区是 792×592；相机视口跟随内容区而不是整个客户区。
        Assert.AreEqual(792d, minimap.ViewportWidth, 1d);
        Assert.AreEqual(592d, minimap.ViewportHeight, 1d);

        using var host = new VisualHost { Size = new DrawingSize(200, 140), DpiScaleOverride = 1f };
        host.Root = minimap;
        using var bitmap = new Bitmap(200, 140);
        using var graphics = Graphics.FromImage(bitmap);
        host.RenderTo(graphics, new DrawingSize(200, 140));
        Assert.AreEqual(200, bitmap.Width);
    }

    [TestMethod]
    public void XamlRuntime_ParsesArgbAndRgb()
    {
        Assert.AreEqual(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), XamlRuntime.ParseColor("#FF102030"));
        Assert.AreEqual(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), XamlRuntime.ParseColor("#102030"));
    }

    // 模板自有的完整装饰器：适配器只留 IWorkflowGridDecorator 契约与共享的网格数学。
    private sealed class TemplateGridDecorator : VisualElement, IWorkflowGridDecorator
    {
        public double ScrollOffsetX { get; set; }

        public double ScrollOffsetY { get; set; }

        public double ContentOffsetX { get; set; }

        public double ContentOffsetY { get; set; }

        public double RulerBand => 36d;

        protected override DrawingSize MeasureOverride(DrawingSize availableSize) => availableSize;

        protected override void OnRender(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillRectangle(Brushes.Black, 0f, 0f, Bounds.Width, Bounds.Height);

            var worldLeft = WorkflowSurfaceMath.GridCanvasLeft(ScrollOffsetX, ContentOffsetX);
            var first = WorkflowSurfaceMath.GridFirstLine(worldLeft, 40);
            for (var value = first; value <= worldLeft + Bounds.Width + 40; value += 40)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, RulerBand);
                using var pen = new Pen(IsMajorLine(value, 200) ? Color.Gray : Color.DimGray);
                graphics.DrawLine(pen, x, 0f, x, Bounds.Height);
            }
        }

        private static bool IsMajorLine(double value, double majorStep)
        {
            var remainder = value % majorStep;
            return Math.Abs(remainder) < 0.001
                   || Math.Abs(remainder - majorStep) < 0.001
                   || Math.Abs(remainder + majorStep) < 0.001;
        }
    }
}
