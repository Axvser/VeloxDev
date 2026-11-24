using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public sealed class MinimapPipelineTests
{
    [TestMethod]
    public void TreeSubscriptionTracksNodesLinksAndSlotAnchors()
    {
        var first = NewNode(0, 0);
        var second = NewNode(3000, 2000);
        var link = new StubLink();
        var tree = new StubTree();
        tree.Nodes.Add(first);
        tree.Nodes.Add(second);
        tree.Links.Add(link);

        using var scene = new Scene(tree);

        Assert.AreEqual(2, scene.Pipeline.SubscribedNodeCount);
        Assert.AreEqual(1, scene.Pipeline.SubscribedLinkCount);

        scene.Pipeline.Refresh();
        Assert.IsFalse(scene.Pipeline.IsDirty);

        first.OnPropertyChanged(nameof(INodeComponent.Anchor));
        Assert.IsTrue(scene.Pipeline.IsDirty, "a node anchor change must invalidate the mapping");

        scene.Pipeline.Refresh();
        first.OnPropertyChanged(nameof(INodeComponent.Size));
        Assert.IsTrue(scene.Pipeline.IsDirty, "a node size change must invalidate the mapping");

        scene.Pipeline.Refresh();
        ((StubSlot)link.Sender).OnPropertyChanged(nameof(ISlotComponent.Anchor));
        Assert.IsTrue(scene.Pipeline.IsDirty, "a link endpoint anchor change must invalidate the mapping");

        scene.Pipeline.Refresh();
        first.OnPropertyChanged("Unrelated");
        Assert.IsFalse(scene.Pipeline.IsDirty, "unrelated property changes must not invalidate the mapping");
    }

    [TestMethod]
    public void CollectionChangesSubscribeAndUnsubscribe()
    {
        var tree = new StubTree();
        var first = NewNode(0, 0);
        tree.Nodes.Add(first);

        using var scene = new Scene(tree);
        Assert.AreEqual(1, scene.Pipeline.SubscribedNodeCount);

        var second = NewNode(100, 100);
        tree.Nodes.Add(second);
        Assert.AreEqual(2, scene.Pipeline.SubscribedNodeCount);
        Assert.IsTrue(scene.Pipeline.IsDirty);

        tree.Nodes.Remove(first);
        Assert.AreEqual(1, scene.Pipeline.SubscribedNodeCount);

        scene.Pipeline.Refresh();
        first.OnPropertyChanged(nameof(INodeComponent.Anchor));
        Assert.IsFalse(scene.Pipeline.IsDirty, "a removed node must no longer invalidate the mapping");

        tree.Links.Add(new StubLink());
        Assert.AreEqual(1, scene.Pipeline.SubscribedLinkCount);

        tree.Links.Clear();
        Assert.AreEqual(0, scene.Pipeline.SubscribedLinkCount);
    }

    [TestMethod]
    public void DisposingTheTreeStopsTracking()
    {
        var node = NewNode(0, 0);
        var tree = new StubTree();
        tree.Nodes.Add(node);

        using var scene = new Scene(tree);
        scene.Pipeline.Tree = null;

        Assert.AreEqual(0, scene.Pipeline.SubscribedNodeCount);
        scene.Pipeline.Refresh();
        node.OnPropertyChanged(nameof(INodeComponent.Anchor));
        Assert.IsFalse(scene.Pipeline.IsDirty);
    }

    [TestMethod]
    public void MappingFitsContentAndViewportIntoThePlacedRectangle()
    {
        var tree = new StubTree();
        tree.Nodes.Add(NewNode(40, 40));
        tree.Nodes.Add(NewNode(320, 40));

        using var scene = new Scene(tree);
        scene.Viewport.ViewportWidth = 200d;
        scene.Viewport.ViewportHeight = 100d;
        scene.Pipeline.Refresh();

        var context = scene.Pipeline.DrawContext;
        Assert.IsNotNull(context);
        Assert.AreEqual(40d, context.ContentBounds.Left, 1e-9);
        Assert.AreEqual(40d, context.ContentBounds.Top, 1e-9);
        Assert.AreEqual(440d, context.ContentBounds.Width, 1e-9);
        Assert.AreEqual(90d, context.ContentBounds.Height, 1e-9);

        var rect = scene.Pipeline.MinimapRect;
        Assert.AreEqual(588d, rect.Left, 1e-9);
        Assert.AreEqual(12d, rect.Top, 1e-9);
        Assert.AreEqual(200d, rect.Width, 1e-9);
        Assert.AreEqual(140d, rect.Height, 1e-9);

        var (expectedOx, expectedOy, expectedScale) = WorkflowSurfaceMath.MinimapFit(
            context.ContentBounds.Width, context.ContentBounds.Height, 196d, 136d, 2d);
        Assert.AreEqual(expectedOx, context.OriginX, 1e-9);
        Assert.AreEqual(expectedOy, context.OriginY, 1e-9);
        Assert.AreEqual(expectedScale, context.Scale, 1e-9);

        var (nodeX, nodeY) = WorkflowSurfaceMath.MinimapLocal(
            context.ContentBounds.Left, context.ContentBounds.Top, context.ContentBounds.Left,
            context.ContentBounds.Top, expectedOx, expectedOy, expectedScale);
        Assert.AreEqual(nodeX, context.MappedNodes[0].Left, 1e-9);
        Assert.AreEqual(nodeY, context.MappedNodes[0].Top, 1e-9);
        Assert.AreEqual(
            WorkflowSurfaceMath.MinThumbSize(160d, expectedScale, 2d),
            context.MappedNodes[0].Width,
            1e-9);

        Assert.IsTrue(context.HasViewport);
        Assert.AreEqual(0d, context.MappedViewport.Left, 1e-9, "the indicator is clamped inside the minimap");
        Assert.AreEqual(
            Math.Min(200d, Math.Max(2d, 200d * expectedScale)),
            context.MappedViewport.Width,
            1e-9);
        Assert.AreEqual(
            Math.Min(140d, Math.Max(2d, 100d * expectedScale)),
            context.MappedViewport.Height,
            1e-9);
    }

    [TestMethod]
    public void PlacementClampsToTheAvailableCorner()
    {
        var tree = new StubTree();
        tree.Nodes.Add(NewNode(0, 0));

        using var scene = new Scene(tree);
        scene.Pipeline.Metrics = MinimapMetrics.Default with
        {
            Width = 400d,
            Height = 400d,
            Available = new SurfaceAvailableArea
            {
                Right = 300d,
                Top = 20d,
                Bottom = 180d,
                Margin = 12d,
            },
        };

        var rect = scene.Pipeline.MinimapRect;
        Assert.AreEqual(12d, rect.Left, 1e-9, "the minimap is pushed left by the free-area clamp");
        Assert.AreEqual(32d, rect.Top, 1e-9);
        Assert.AreEqual(276d, rect.Width, 1e-9);
        Assert.AreEqual(136d, rect.Height, 1e-9);
    }

    [TestMethod]
    public void RenderCallsTheRendererOnEveryPassAndRespectsVisibility()
    {
        var tree = new StubTree();
        tree.Nodes.Add(NewNode(0, 0));

        using var scene = new Scene(tree);
        scene.Pipeline.Render(new object());
        Assert.AreEqual(1, scene.Renderer.Renders.Count);
        Assert.IsFalse(scene.Pipeline.IsDirty);

        // 平台每次重绘都走 Renderer；dirty 只负责决定要不要重算映射。
        var cached = scene.Pipeline.DrawContext;
        scene.Pipeline.Render(new object());
        Assert.AreEqual(2, scene.Renderer.Renders.Count);
        Assert.AreSame(cached, scene.Pipeline.DrawContext);

        scene.Pipeline.IsVisible = false;
        scene.Pipeline.Render(new object());
        Assert.AreEqual(2, scene.Renderer.Renders.Count, "a hidden minimap does not draw");

        scene.Pipeline.IsVisible = true;
        scene.Pipeline.Render(new object());
        Assert.AreEqual(3, scene.Renderer.Renders.Count);
    }

    [TestMethod]
    public void EmptyTreeProducesNoDrawContext()
    {
        using var scene = new Scene(new StubTree());
        scene.Pipeline.Refresh();

        Assert.IsNull(scene.Pipeline.DrawContext);
        scene.Pipeline.Render(new object());
        Assert.AreEqual(0, scene.Renderer.Renders.Count);
    }

    [TestMethod]
    public void PointerPressOutsideTheMinimapIsIgnored()
    {
        var tree = new StubTree();
        tree.Nodes.Add(NewNode(0, 0));

        using var scene = new Scene(tree);
        scene.Input.PointerPosition = (10d, 10d);
        scene.Input.IsPressed = true;

        Assert.IsFalse(scene.Pipeline.PointerPressed());
        Assert.IsFalse(scene.Pipeline.IsDragging);
        Assert.AreEqual(0, scene.Input.CaptureCount);
    }

    [TestMethod]
    public void PointerPressOnTheMinimapCentersTheViewport()
    {
        var tree = new StubTree();
        tree.Nodes.Add(NewNode(0, 0));
        tree.Nodes.Add(NewNode(3000, 2000));

        using var scene = new Scene(tree);
        scene.Pipeline.Refresh();
        var context = scene.Pipeline.DrawContext!;
        var rect = scene.Pipeline.MinimapRect;
        var pointerX = rect.Left + 100d;
        var pointerY = rect.Top + 70d;

        var (wcx, wcy) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            pointerX - rect.Left, pointerY - rect.Top,
            context.OriginX, context.OriginY, context.Scale,
            context.ContentBounds.Left, context.ContentBounds.Top);
        var (expectedX, expectedY) = WorkflowSurfaceMath.MinimapToScroll(
            wcx, wcy, scene.Viewport.ViewportWidth, scene.Viewport.ViewportHeight, 0d, 0d);

        scene.Input.PointerPosition = (pointerX, pointerY);
        scene.Input.IsPressed = true;

        Assert.IsTrue(scene.Pipeline.PointerPressed());
        Assert.IsTrue(scene.Pipeline.IsDragging);
        Assert.AreEqual(1, scene.Input.CaptureCount);
        Assert.AreEqual(1, scene.Viewport.Applied.Count);
        Assert.AreEqual(expectedX, scene.Viewport.Applied[0].X, 1e-6);
        Assert.AreEqual(expectedY, scene.Viewport.Applied[0].Y, 1e-6);

        Assert.IsTrue(scene.Viewport.Applied[0].X > 0d, "the scenario must navigate to a positive offset");
        Assert.IsTrue(scene.Viewport.Applied[0].Y > 0d, "the scenario must navigate to a positive offset");
    }

    [TestMethod]
    public void PointerMoveAndReleaseDriveTheDrag()
    {
        var tree = new StubTree();
        tree.Nodes.Add(NewNode(0, 0));
        tree.Nodes.Add(NewNode(3000, 2000));

        using var scene = new Scene(tree);
        var rect = scene.Pipeline.MinimapRect;
        scene.Input.PointerPosition = (rect.Left + 100d, rect.Top + 70d);
        scene.Input.IsPressed = true;
        Assert.IsTrue(scene.Pipeline.PointerPressed());

        scene.Input.PointerPosition = (rect.Left + 140d, rect.Top + 90d);
        scene.Pipeline.PointerMoved();

        Assert.AreEqual(2, scene.Viewport.Applied.Count);
        Assert.AreNotEqual(scene.Viewport.Applied[0], scene.Viewport.Applied[1]);

        scene.Pipeline.PointerReleased();
        Assert.IsFalse(scene.Pipeline.IsDragging);
        Assert.AreEqual(1, scene.Input.ReleaseCount);

        scene.Pipeline.PointerReleased();
        Assert.AreEqual(1, scene.Input.ReleaseCount, "releasing twice must not release the pointer twice");
    }

    [TestMethod]
    public void ClampRectOverridesThePlacedRectangleForHitTesting()
    {
        var tree = new StubTree();
        tree.Nodes.Add(NewNode(0, 0));

        using var scene = new Scene(tree);
        scene.Input.ClampRect = WorkflowBounds.FromNode(0d, 0d, 10d, 10d);
        scene.Input.PointerPosition = (5d, 5d);
        scene.Input.IsPressed = true;

        Assert.IsTrue(scene.Pipeline.PointerPressed(), "the input host's clamp rectangle is the interactive region");

        scene.Pipeline.PointerReleased();
        scene.Input.ClampRect = default;
        scene.Input.PointerPosition = (5d, 5d);

        Assert.IsFalse(scene.Pipeline.PointerPressed(), "without a clamp rectangle the placed minimap is the region");
    }

    [TestMethod]
    public void InvalidateRaisesRenderRequested()
    {
        using var scene = new Scene(new StubTree());
        var raised = 0;
        scene.Pipeline.RenderRequested += (_, _) => raised++;

        scene.Pipeline.Refresh();
        scene.Pipeline.Invalidate();

        Assert.AreEqual(1, raised);
        Assert.IsTrue(scene.Pipeline.IsDirty);
    }

    private static StubNode NewNode(double x, double y)
        => new()
        {
            Anchor = new Anchor(x, y, 0),
            Size = new Size(160d, 90d),
        };

    private sealed class FakeRenderer : IMinimapRenderer<object>
    {
        public List<MinimapDrawContext> Renders { get; } = [];

        public void Render(object surface, MinimapDrawContext context) => Renders.Add(context);
    }

    private sealed class FakeViewport : IMinimapViewportHost
    {
        public double ScrollOffsetX { get; set; }
        public double ScrollOffsetY { get; set; }
        public double ContentOffsetX { get; set; }
        public double ContentOffsetY { get; set; }
        public double ViewportWidth { get; set; } = 800d;
        public double ViewportHeight { get; set; } = 600d;
        public double ScrollableWidth { get; set; } = 7200d;
        public double ScrollableHeight { get; set; } = 5400d;
        public List<(double X, double Y)> Applied { get; } = [];

        public void ApplyScroll(double x, double y) => Applied.Add((x, y));
    }

    private sealed class FakeInput : IMinimapInputHost
    {
        public (double X, double Y) PointerPosition { get; set; }
        public bool IsPressed { get; set; }
        public bool IsCaptured { get; private set; }
        public WorkflowBounds ClampRect { get; set; }
        public int CaptureCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public bool Capture()
        {
            CaptureCount++;
            IsCaptured = true;
            return true;
        }

        public void Release()
        {
            ReleaseCount++;
            IsCaptured = false;
        }
    }

    private sealed class Scene : IDisposable
    {
        public Scene(StubTree tree)
        {
            Renderer = new FakeRenderer();
            Viewport = new FakeViewport();
            Input = new FakeInput();
            Pipeline = new MinimapPipelineCore<object, FakeRenderer, FakeViewport, FakeInput>(
                Renderer,
                Viewport,
                Input,
                MinimapMetrics.Default with
                {
                    Available = new SurfaceAvailableArea { Right = 800d, Bottom = 600d, Margin = 12d },
                })
            {
                Tree = tree,
            };
        }

        public FakeRenderer Renderer { get; }

        public FakeViewport Viewport { get; }

        public FakeInput Input { get; }

        public MinimapPipelineCore<object, FakeRenderer, FakeViewport, FakeInput> Pipeline { get; }

        public void Dispose() => Pipeline.Dispose();
    }

    private sealed class StubLink : ILinkComponent
    {
        public ISlotComponent Sender { get; set; } = new StubSlot();
        public ISlotComponent Receiver { get; set; } = new StubSlot();
        public bool IsVisible { get; set; }
        public IVeloxCommand DeleteCommand { get; } = new StubCommand();
        public IVeloxCommand CloseCommand { get; } = new StubCommand();
        public event PropertyChangingEventHandler? PropertyChanging;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void InitializeWorkflow()
        {
        }

        public void OnPropertyChanging(string propertyName)
            => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));

        public void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ILinkHelper GetHelper() => throw new NotSupportedException();

        public void SetHelper(ILinkHelper helper)
        {
        }
    }
}
