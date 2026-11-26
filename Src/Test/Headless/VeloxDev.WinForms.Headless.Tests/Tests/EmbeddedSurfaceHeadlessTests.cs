using System.Drawing;
using System.Windows.Forms;
using Easel.Visuals;
using VeloxDev.AdapterTestKit;
using VeloxDev.WinForms.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using DrawingPointF = System.Drawing.PointF;
using DrawingSize = System.Drawing.Size;
using VisualBorder = Easel.Visuals.Controls.Border;
using VisualCanvas = Easel.Visuals.Controls.Canvas;
using VisualColumnDefinition = Easel.Controls.ColumnDefinition;
using VisualGrid = Easel.Visuals.Controls.Grid;
using VisualGridLength = Easel.Controls.GridLength;
using VisualRowDefinition = Easel.Controls.RowDefinition;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// A workbench paints chrome around the workflow surface in the same VisualHost. The behavior must use the named
// surface subtree's own bounds and coordinate origin; the host client rectangle is no longer the surface.
[STATestClass]
public sealed class EmbeddedSurfaceHeadlessTests
{
    private const int ChromeWidth = 48;
    private const int ChromeHeight = 32;

    [TestMethod]
    public void EmbeddedSurface_UsesItsOwnOriginForTheCamera()
    {
        using var host = new EmbeddedSurfaceHost();
        var graph = TestGraph.Create();
        host.Attach(graph);

        var origin = host.Surface.AbsoluteBounds.Location;
        Assert.AreEqual(ChromeWidth, origin.X, 0.5f, "the surface sits to the right of the activity bar");
        Assert.AreEqual(ChromeHeight, origin.Y, 0.5f, "the surface sits below the title strip");

        var topLeft = WorkflowSurfaceBehavior.SurfaceToWorld(
            host.SurfaceView,
            new DrawingPointF(origin.X, origin.Y));
        Assert.AreEqual(0f, topLeft.X, 0.5f, "the surface's own top-left is world (0,0)");
        Assert.AreEqual(0f, topLeft.Y, 0.5f);

        var roundTrip = WorkflowSurfaceBehavior.WorldToSurface(host.SurfaceView, new DrawingPointF(123.5f, 67.25f));
        Assert.AreEqual(origin.X + 123.5f, roundTrip.X, 0.5f);
        Assert.AreEqual(origin.Y + 67.25f, roundTrip.Y, 0.5f);
    }

    [TestMethod]
    public void EmbeddedSurface_ResolvesPartsInsideItsOwnSubtree()
    {
        using var host = new EmbeddedSurfaceHost();
        var graph = TestGraph.Create();
        host.Attach(graph);

        // The shell carries a decoy canvas with the same part name; only the surface's canvas may receive views.
        Assert.IsNotNull(host.DecoyCanvas, "the shell decoy must exist to make the scope observable");
        Assert.IsTrue(
            host.SurfaceView.Canvas.Children.Count > 0,
            "the pool must materialize into the surface's canvas, not the shell decoy");
        Assert.AreEqual(
            0,
            host.DecoyCanvas!.Children.Count,
            "the shell canvas must not receive workflow views");
    }

    [TestMethod]
    public void EmbeddedSurface_HitTestsInHostCoordinates()
    {
        using var host = new EmbeddedSurfaceHost();
        var graph = TestGraph.Create();
        host.Attach(graph);

        var center = new DrawingPointF(
            (float)(graph.Left.Anchor.Horizontal + (graph.Left.Size.Width / 2d)),
            (float)(graph.Left.Anchor.Vertical + (graph.Left.Size.Height / 2d)));
        var hostPoint = WorkflowSurfaceBehavior.WorldToSurface(host.SurfaceView, center);

        host.ProcessPointerDown(hostPoint, MouseButtons.Left);

        Assert.AreSame(
            graph.Left,
            WorkflowInput.For(graph.Tree).PointerTarget,
            "a press at the surface's host offset must still reach the node under it");
        host.ProcessPointerUp(hostPoint, MouseButtons.Left);
    }

    [TestMethod]
    public void EmbeddedSurface_ReportsItsOwnViewport()
    {
        using var host = new EmbeddedSurfaceHost();
        var graph = TestGraph.Create();
        host.Attach(graph);

        var viewport = graph.Tree.Helper.Viewport;

        Assert.AreEqual(host.SurfaceView.ScrollViewer.ViewportWidth, viewport.Width, 1d,
            "virtualisation must use the surface's scroll viewport, not the host client width");
        Assert.AreEqual(host.SurfaceView.ScrollViewer.ViewportHeight, viewport.Height, 1d);
        Assert.IsTrue(viewport.Width < host.LogicalClientSize.Width,
            "the shell must actually take room from the surface");
    }

    private sealed class EmbeddedSurfaceHost : VisualHost
    {
        public EmbeddedSurfaceHost()
        {
            Size = new DrawingSize(800, 600);
            DpiScaleOverride = 1f;

            var shell = new VisualGrid();
            shell.RowDefinitions.Add(new VisualRowDefinition { Height = new VisualGridLength(ChromeHeight) });
            shell.RowDefinitions.Add(new VisualRowDefinition { Height = VisualGridLength.Star });
            shell.ColumnDefinitions.Add(new VisualColumnDefinition { Width = new VisualGridLength(ChromeWidth) });
            shell.ColumnDefinitions.Add(new VisualColumnDefinition { Width = VisualGridLength.Star });

            var title = new VisualBorder { Name = "TitleStrip" };
            VisualGrid.SetRow(title, 0);
            VisualGrid.SetColumnSpan(title, 2);
            shell.Add(title);

            var activity = new VisualBorder { Name = "ActivityBar" };
            VisualGrid.SetRow(activity, 1);
            VisualGrid.SetColumn(activity, 0);
            shell.Add(activity);

            DecoyCanvas = new VisualCanvas { Name = "PART_Canvas" };
            var decoyHost = new VisualBorder { Name = "ShellDecoy" };
            decoyHost.Add(DecoyCanvas);
            VisualGrid.SetRow(decoyHost, 0);
            VisualGrid.SetColumn(decoyHost, 0);
            shell.Add(decoyHost);

            SurfaceView = new TestWorkflowSurfaceView { Name = "WorkflowSurface" };
            VisualGrid.SetRow(SurfaceView, 1);
            VisualGrid.SetColumn(SurfaceView, 1);
            shell.Add(SurfaceView);

            Root = shell;
            ViewPool.SetTemplateSelector(SurfaceView.Canvas, new TestVisualTemplateSelector());

            // The controller defaults SurfaceName to the attached root's name when it is not the host root.
            WorkflowSurfaceBehavior.SetScrollViewerName(SurfaceView, "PART_ScrollViewer");
            WorkflowSurfaceBehavior.SetCanvasName(SurfaceView, "PART_Canvas");
            WorkflowSurfaceBehavior.SetGridDecoratorName(SurfaceView, "PART_GridDecorator");
        }

        public TestWorkflowSurfaceView SurfaceView { get; }

        public VisualElement Surface => SurfaceView;

        public VisualCanvas? DecoyCanvas { get; }

        public void Attach(TestGraph graph)
        {
            CreateControl();
            PerformLayout();
            Render();

            // The host's data context is inherited into the surface root; it is the tree's only source.
            DataContext = graph.Tree;
            ViewPool.SetItemsSource(SurfaceView.Canvas, graph.Tree.GetHelper().VisibleItems);
            WorkflowSurfaceBehavior.SetIsEnabled(SurfaceView, true);
            Render();
        }

        private void Render()
        {
            var size = LogicalClientSize;
            using var bitmap = new Bitmap(size.Width, size.Height);
            using var graphics = Graphics.FromImage(bitmap);
            RenderTo(graphics, size);
            RenderTo(graphics, size);
            Application.DoEvents();
        }
    }
}
