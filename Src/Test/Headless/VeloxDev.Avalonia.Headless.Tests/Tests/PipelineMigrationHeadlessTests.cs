using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using VeloxDev.AdapterTestKit;
using VeloxDev.Avalonia.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.ViewHosting;
using Behaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Avalonia.Headless.Tests.Tests;

/// <summary>
/// Covers the three Core pipelines as the Avalonia adapter now hosts them: the minimap state machine driven
/// through its standalone layers, the surface's single metrics snapshot reaching the overlays, and the slot
/// pipeline measuring against a canvas coordinate host.
/// </summary>
[TestClass]
public sealed class PipelineMigrationHeadlessTests
{
    private static HeadlessUnitTestSession _session = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
        => _session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    [ClassCleanup]
    public static void ClassCleanup() => _session.Dispose();

    private static void Run(Action test)
        => _session.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();

    [TestMethod]
    public void MinimapPipeline_Drag_MovesTheSurfaceScrollViewer()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);
            surface.Scroll.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var viewport = new MinimapViewportHost(surface.Scroll);
            var input = new MinimapInputHost();
            using var pipeline = new MinimapPipeline(
                new NullRenderer(),
                viewport,
                input,
                new MinimapMetrics
                {
                    Width = 200d,
                    Height = 140d,
                    MinSize = 20d,
                    Padding = 2d,
                    NodeCornerRadius = 1d,
                    MinimapCornerRadius = 4d,
                    MinimumThumbSize = 2d,
                    Available = new SurfaceAvailableArea
                    {
                        Right = 800d,
                        Top = 0d,
                        Bottom = 600d,
                        Margin = 12d,
                    },
                })
            {
                Tree = graph.Tree,
            };

            pipeline.Refresh();
            var context = pipeline.DrawContext!;
            var rect = pipeline.MinimapRect;
            Assert.AreEqual(588d, rect.Left, 1e-9);
            Assert.AreEqual(12d, rect.Top, 1e-9);

            var (expectedX, expectedY) = ExpectedScroll(context, rect, 180d, 120d, viewport);

            input.PointerPosition = (rect.Left + 180d, rect.Top + 120d);
            input.IsPressed = true;

            Assert.IsTrue(pipeline.PointerPressed());
            Assert.IsTrue(pipeline.IsDragging);

            surface.Scroll.UpdateLayout();
            Assert.AreEqual(expectedX, surface.Scroll.Offset.X, 1d);
            Assert.AreEqual(expectedY, surface.Scroll.Offset.Y, 1d);

            var before = surface.Scroll.Offset.X;
            input.PointerPosition = (rect.Left + 60d, rect.Top + 40d);
            pipeline.PointerMoved();
            surface.Scroll.UpdateLayout();

            Assert.AreNotEqual(before, surface.Scroll.Offset.X, "dragging to another point must scroll again");

            pipeline.PointerReleased();
            Assert.IsFalse(pipeline.IsDragging);
        });

    [TestMethod]
    public void Surface_PushesOneMetricsSnapshotIntoTheMinimap()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph, withMinimap: true);
            var minimap = surface.Minimap!;

            Assert.AreSame(graph.Tree, minimap.WorkflowTree, "the surface must bind the tree into the minimap");
            Assert.AreEqual(surface.Scroll.Viewport.Width, minimap.ViewportWidth, 1e-9);
            Assert.AreEqual(surface.Scroll.Viewport.Height, minimap.ViewportHeight, 1e-9);
            Assert.AreEqual(
                graph.Tree.Layout.ActualOffset.Horizontal,
                minimap.ContentOffsetX,
                1e-9,
                "the minimap content offset comes from the same snapshot as the grid decorator");
            Assert.AreEqual(
                graph.Tree.Layout.ActualOffset.Horizontal,
                surface.Decorator.ContentOffsetX,
                1e-9);

            Assert.IsTrue(
                minimap.AvailableRight > 0d && minimap.AvailableRight <= surface.Window.Width,
                "the available right edge stays inside the surface, clear of the vertical scrollbar");
            Assert.IsTrue(
                minimap.AvailableBottom > 0d && minimap.AvailableBottom <= surface.Window.Height,
                "the available bottom edge stays inside the surface, clear of the horizontal scrollbar");

            surface.ScrollTo(120d, 80d);

            Assert.AreEqual(120d, minimap.ScrollOffsetX, 0.5);
            Assert.AreEqual(80d, minimap.ScrollOffsetY, 0.5);
        });

    [TestMethod]
    public void SlotLayout_WithACoordinateHost_WritesScrollSpaceAnchors()
        => Run(() =>
        {
            var tree = new TreeDefaultViewModel();
            var node = new NodeDefaultViewModel
            {
                Anchor = new Anchor(40d, 40d, 0),
                Size = new VeloxDev.WorkflowSystem.Size(160d, 90d),
            };
            tree.GetHelper().CreateNode(node);

            var slot = new SlotDefaultViewModel();
            node.GetHelper().CreateSlot(slot);

            var slotBorder = new Border
            {
                Width = 16d,
                Height = 16d,
                DataContext = slot,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(8d),
            };

            var grid = new Grid();
            grid.Children.Add(slotBorder);

            var view = new UserControl
            {
                Width = 160d,
                Height = 90d,
                DataContext = node,
                Content = grid,
            };

            var scope = new NameScope();
            NameScope.SetNameScope(view, scope);
            scope.Register("PART_Slot", slotBorder);
            scope.Complete();

            var canvas = new Canvas { Width = 400d, Height = 300d, Name = "PART_Canvas" };
            Canvas.SetLeft(view, 40d);
            Canvas.SetTop(view, 40d);
            canvas.Children.Add(view);

            Behaviors.WorkflowSlotLayoutBehavior.SetSlotNames(view, "PART_Slot");
            Behaviors.WorkflowSlotLayoutBehavior.SetCoordinateHostName(view, "PART_Canvas");
            Behaviors.WorkflowSlotLayoutBehavior.SetIsEnabled(view, true);

            var window = new Window { Width = 400d, Height = 300d, Content = canvas };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // view at canvas (40,40); margin 8 + half 8 = centre (56,56); ActualOffset is still zero.
            Assert.AreEqual(56d, slot.Anchor.Horizontal, 1d);
            Assert.AreEqual(56d, slot.Anchor.Vertical, 1d);

            window.Close();
        });

    private static (double X, double Y) ExpectedScroll(
        MinimapDrawContext context,
        WorkflowBounds rect,
        double minimapX,
        double minimapY,
        IMinimapViewportHost viewport)
    {
        var pad = context.Padding;
        var (ox, oy, scale) = WorkflowSurfaceMath.MinimapFit(
            context.ContentBounds.Width,
            context.ContentBounds.Height,
            rect.Width - pad * 2,
            rect.Height - pad * 2,
            pad);
        var (canvasX, canvasY) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            minimapX, minimapY, ox, oy, scale, context.ContentBounds.Left, context.ContentBounds.Top);
        return WorkflowSurfaceMath.MinimapToScroll(
            canvasX, canvasY, viewport.ViewportWidth, viewport.ViewportHeight,
            viewport.ContentOffsetX, viewport.ContentOffsetY);
    }

    private sealed class NullRenderer : IMinimapRenderer<DrawingContext>
    {
        public void Render(DrawingContext surface, MinimapDrawContext context)
        {
        }
    }
}
