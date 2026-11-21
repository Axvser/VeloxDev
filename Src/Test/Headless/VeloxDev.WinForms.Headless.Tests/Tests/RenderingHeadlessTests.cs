using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// 自绘控件的 OnPaint/OnPaintBackground 可以在无窗口进程里用 DrawToBitmap 触发。
[STATestClass]
public sealed class RenderingHeadlessTests
{
    [TestMethod]
    public void TemplateGridDecorator_PaintsWithoutAWindow()
    {
        using var decorator = new TemplateGridDecorator { Width = 200, Height = 100 };
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

    // 模板自有的完整装饰器：适配器只留 IWorkflowGridDecorator 契约与共享的网格数学。
    private sealed class TemplateGridDecorator : Panel, IWorkflowGridDecorator
    {
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ScrollOffsetX { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ScrollOffsetY { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ContentOffsetX { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ContentOffsetY { get; set; }

        public double RulerBand => 36;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(Brushes.Black, ClientRectangle);

            var worldLeft = WorkflowSurfaceMath.GridCanvasLeft(ScrollOffsetX, ContentOffsetX);
            var first = WorkflowSurfaceMath.GridFirstLine(worldLeft, 40);
            for (var value = first; value <= worldLeft + Width + 40; value += 40)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, RulerBand);
                using var pen = new Pen(
                    WorkflowSurfaceGrid.IsMajorLine(value, 200) ? Color.Gray : Color.DimGray);
                g.DrawLine(pen, x, 0, x, Height);
            }
        }
    }
}
