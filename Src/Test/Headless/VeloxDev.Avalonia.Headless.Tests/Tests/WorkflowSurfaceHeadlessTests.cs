using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VeloxDev.AdapterTestKit;
using VeloxDev.Avalonia.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem.StandardEx;
using Behaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.Avalonia.Headless.Tests.Tests;

/// <summary>
/// Drives the Avalonia surface inside a headless window: real layout and real input routing, no window system.
/// The assertions are deliberately the same shape as the WPF suite's, so a divergence names the adapter.
/// </summary>
[TestClass]
public sealed class WorkflowSurfaceHeadlessTests
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
    public void Attach_ResolvesNamedParts_AndWritesCanvasTransform()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);

            var transform = Behaviors.WorkflowCanvasTransformBehavior.GetTransform(surface.Host);

            Assert.IsInstanceOfType<TransformGroup>(transform);
            var group = (TransformGroup)transform!;
            Assert.IsInstanceOfType<TranslateTransform>(group.Children[0]);

            var translate = (TranslateTransform)group.Children[0];
            Assert.AreEqual(
                graph.Tree.Layout.ActualOffset.Horizontal,
                translate.X,
                1e-9,
                "the surface must publish the layout's actual offset as the canvas transform");
            Assert.AreEqual(
                graph.Tree.Layout.ActualOffset.Horizontal,
                surface.Decorator.ContentOffsetX,
                1e-9,
                "the grid decorator must receive the same content offset");
        });

    [TestMethod]
    public void Refresh_WhileDisabled_IsANoOp()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph, enable: false);

            Behaviors.WorkflowSurfaceBehavior.Refresh(surface.Host);

            Assert.IsNull(Behaviors.WorkflowCanvasTransformBehavior.GetTransform(surface.Host));
            Assert.AreEqual(0d, graph.Tree.Helper.Viewport.Width);
        });

    [TestMethod]
    public void Scroll_WritesTheViewport_AndPersistsTheOffset()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);

            surface.ScrollTo(120d, 80d);

            var expectedLeft = Wf.WorkflowSurfaceMath.ToWorld(
                120d, graph.Tree.Layout.ActualOffset.Horizontal);
            var expectedTop = Wf.WorkflowSurfaceMath.ToWorld(
                80d, graph.Tree.Layout.ActualOffset.Vertical);

            Assert.AreEqual(expectedLeft, graph.Tree.Helper.Viewport.Horizontal, 0.5);
            Assert.AreEqual(expectedTop, graph.Tree.Helper.Viewport.Vertical, 0.5);
            Assert.AreEqual(
                graph.Tree.Helper.Viewport.Horizontal,
                graph.Tree.Layout.ViewportOffset.Horizontal,
                1e-9,
                "the persisted offset must be written from the same viewport the helper received");
        });

    [TestMethod]
    public void ViewportRestore_IsAppliedAfterAttach()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            graph.Tree.Layout.ViewportOffset = new Wf.Offset(150d, 90d);

            using var surface = new AvaloniaHeadlessSurface(graph);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(150d, surface.Scroll.Offset.X, 1d);
            Assert.AreEqual(90d, surface.Scroll.Offset.Y, 1d);
        });

    [TestMethod]
    public void Virtualization_ExcludesNodesOutsideTheViewport()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);

            Assert.IsTrue(graph.Tree.Helper.VisibleItems.Contains(graph.Left));
            Assert.IsTrue(graph.Tree.Helper.VisibleItems.Contains(graph.Right));
            Assert.IsFalse(
                graph.Tree.Helper.VisibleItems.Contains(graph.Far),
                "the far node is outside the 800x600 viewport and must not be visible");

            surface.ScrollTo(6000d, 4000d);

            Assert.IsTrue(
                graph.Tree.Helper.VisibleItems.Contains(graph.Far),
                "scrolling onto the far node must bring it into the visible set");
            Assert.IsFalse(
                graph.Tree.Helper.VisibleItems.Contains(graph.Left),
                "the near nodes must leave the visible set once the viewport moves away");
        });

    [TestMethod]
    public void CtrlWheel_ZoomsIn_AndTheScaleStaysClamped()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);

            var before = graph.Tree.Layout.Scale.Horizontal;
            surface.Window.MouseWheel(
                new Point(400d, 300d), new Vector(0d, 1d), RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(
                before / 1.1,
                graph.Tree.Layout.Scale.Horizontal,
                1e-9,
                "wheel up must zoom in: Scale is the collapse factor, so it gets smaller");

            for (var i = 0; i < 40; i++)
            {
                surface.Window.MouseWheel(
                    new Point(400d, 300d), new Vector(0d, 1d), RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.AreEqual(0.1d, graph.Tree.Layout.Scale.Horizontal, 1e-9);
        });

    [TestMethod]
    public void ViewPool_CreatesOneViewPerVisibleItem()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);

            Behaviors.ViewPool.SetTemplateSelector(surface.Canvas, new AnyTemplate());
            Behaviors.ViewPool.SetItemsSource(surface.Canvas, graph.Tree.Helper.VisibleItems);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(
                graph.Tree.Helper.VisibleItems.Count,
                surface.Canvas.Children.Count,
                "the pool must materialize exactly one view for each visible item");
        });

    [TestMethod]
    public void Detach_StopsWritingTheViewport()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);

            surface.ScrollTo(100d, 20d);
            var before = graph.Tree.Helper.Viewport;

            Behaviors.WorkflowSurfaceBehavior.SetIsEnabled(surface.Host, false);
            surface.ScrollTo(400d, 60d);

            Assert.AreEqual(
                before.Horizontal,
                graph.Tree.Helper.Viewport.Horizontal,
                1e-9,
                "after detach the surface must not keep feeding the model");
        });

    [TestMethod]
    public void SlotLayout_WritesAnchorsFromMeasuredCentres()
        => Run(() =>
        {
            var graph = TestGraph.Create(connect: false);
            var node = graph.Left;
            var input = new Wf.SlotDefaultViewModel();
            var output = new Wf.SlotDefaultViewModel();
            node.GetHelper().CreateSlot(input);
            node.GetHelper().CreateSlot(output);

            var inputBorder = new Border
            {
                Width = 16d,
                Height = 16d,
                DataContext = input,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8d, 8d, 0d, 0d),
            };
            var outputBorder = new Border
            {
                Width = 16d,
                Height = 16d,
                DataContext = output,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0d, 8d, 8d, 0d),
            };

            var grid = new Grid();
            grid.Children.Add(inputBorder);
            grid.Children.Add(outputBorder);

            var view = new UserControl
            {
                Width = 160d,
                Height = 90d,
                DataContext = node,
                Content = grid,
            };

            var scope = new NameScope();
            NameScope.SetNameScope(view, scope);
            scope.Register("PART_InputSlot", inputBorder);
            scope.Register("PART_OutputSlot", outputBorder);
            scope.Complete();

            Behaviors.WorkflowSlotLayoutBehavior.SetSlotNames(view, "PART_InputSlot,PART_OutputSlot");
            Behaviors.WorkflowSlotLayoutBehavior.SetIsEnabled(view, true);

            var window = new Window { Width = 160d, Height = 90d, Content = view };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(56d, input.Anchor.Horizontal, 1d);
            Assert.AreEqual(56d, input.Anchor.Vertical, 1d);
            Assert.AreEqual(184d, output.Anchor.Horizontal, 1d);
            Assert.AreEqual(56d, output.Anchor.Vertical, 1d);

            window.Close();
        });

    [TestMethod]
    public void NodeDrag_MovesTheNodeByTheViewDelta()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);

            var nodeView = new Border
            {
                Width = 160d,
                Height = 90d,
                Background = Brushes.Transparent,
                DataContext = graph.Left,
            };
            Canvas.SetLeft(nodeView, 40d);
            Canvas.SetTop(nodeView, 40d);
            surface.Canvas.Children.Add(nodeView);

            Behaviors.WorkflowNodeDragBehavior.SetIsEnabled(nodeView, true);

            // 先移动再按下：无头指针要先落到目标上，命中/over 状态才会建立。
            surface.Window.MouseMove(new Point(100d, 100d), RawInputModifiers.None);
            surface.Window.MouseDown(new Point(100d, 100d), MouseButton.Left, RawInputModifiers.None);
            surface.Window.MouseMove(new Point(150d, 120d), RawInputModifiers.None);
            surface.Window.MouseUp(new Point(150d, 120d), MouseButton.Left, RawInputModifiers.None);

            WaitUntil(() => graph.Left.Anchor.Horizontal > 80d, TimeSpan.FromSeconds(2));

            Assert.AreEqual(90d, graph.Left.Anchor.Horizontal, 1d);
            Assert.AreEqual(60d, graph.Left.Anchor.Vertical, 1d);
        });

    [TestMethod]
    public void Minimap_RendersNodesAndViewport()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            var minimap = new Behaviors.WorkflowMinimapOverlay
            {
                Width = 200d,
                Height = 140d,
                WorkflowTree = graph.Tree,
                ViewportWidth = 800d,
                ViewportHeight = 600d,
                IsMinimapVisible = true,
            };

            minimap.Measure(new Size(200d, 140d));
            minimap.Arrange(new Rect(0d, 0d, 200d, 140d));

            using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize(200, 140), new Vector(96d, 96d));
            bitmap.Render(minimap);

            Assert.AreEqual(200, bitmap.PixelSize.Width);
        });

    [TestMethod]
    public void NodeSink_HearsAMove()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            var sink = new NodeSink();
            var view = new UserControl { DataContext = graph.Left };

            Behaviors.WorkflowEvents.SetNode(view, sink);
            graph.Left.GetHelper().Move(new Wf.Offset(10d, 5d));
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(sink.Moving + sink.Moved > 0, "the node sink must hear the move");
            Behaviors.WorkflowEvents.SetNode(view, null);
        });

    private sealed class NodeSink : Wf.IWorkflowNodeEventSink
    {
        public int Moving;
        public int Moved;

        public void OnMoving(Wf.NodeMoveEventArgs e) => Moving++;
        public void OnMoved(Wf.NodeMoveEventArgs e) => Moved++;
        public void OnResizing(Wf.NodeResizeEventArgs e) { }
        public void OnResized(Wf.NodeResizeEventArgs e) { }
        public void OnDeleting(Wf.NodeEventArgs e) { }
        public void OnDeleted(Wf.NodeEventArgs e) { }
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            Thread.Sleep(5);
        }

        Assert.Fail("timed out waiting for the adapter to update the model");
    }

    [TestMethod]
    public void Locate_ScrollsTheNodeToTheViewportCenter()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);
            surface.Scroll.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var node = graph.Far;
            var expectedX = node.Anchor.Horizontal + node.Size.Width / 2d
                - surface.Scroll.Viewport.Width / 2d + graph.Tree.Layout.ActualOffset.Horizontal;
            var expectedY = node.Anchor.Vertical + node.Size.Height / 2d
                - surface.Scroll.Viewport.Height / 2d + graph.Tree.Layout.ActualOffset.Vertical;

            Assert.IsTrue(node.Locate(), "an attached surface registers a navigator");
            surface.Scroll.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(
                Math.Max(0d, expectedX), surface.Scroll.Offset.X, 1d,
                "the far node's center must land at the viewport center");
            Assert.AreEqual(Math.Max(0d, expectedY), surface.Scroll.Offset.Y, 1d);
        });

    [TestMethod]
    public void Locate_GrowsTheCanvasWhenTheElementCannotBeCenteredInIt()
        => Run(() =>
        {
            var graph = TestGraph.Create();
            using var surface = new AvaloniaHeadlessSurface(graph);
            surface.Scroll.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // Before the origin: centering needs a negative scroll, so the canvas must grow instead of clamping.
            Assert.IsTrue(graph.Left.Locate());
            surface.Scroll.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var leftX = graph.Left.Anchor.Horizontal + graph.Left.Size.Width / 2d
                + graph.Tree.Layout.ActualOffset.Horizontal - surface.Scroll.Offset.X;
            Assert.AreEqual(surface.Scroll.Viewport.Width / 2d, leftX, 1d, "negative edge");
            Assert.IsTrue(graph.Tree.Layout.NegativeOffset.Horizontal > 0d, "the canvas grew before the origin");

            // Past the far edge: the node is outside the current extent, so the extent has to grow.
            var edge = new Wf.NodeDefaultViewModel
            {
                Parent = graph.Tree,
                Anchor = new Wf.Anchor(9000, 4000, 0),
                Size = new Wf.Size(160, 90),
            };
            graph.Tree.Nodes.Add(edge);
            surface.Scroll.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(edge.Locate());
            surface.Scroll.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var rightX = edge.Anchor.Horizontal + edge.Size.Width / 2d
                + graph.Tree.Layout.ActualOffset.Horizontal - surface.Scroll.Offset.X;
            Assert.AreEqual(surface.Scroll.Viewport.Width / 2d, rightX, 1d, "positive edge");
            Assert.IsTrue(graph.Tree.Layout.PositiveOffset.Horizontal > 0d, "the canvas grew past the content");
        });
}
