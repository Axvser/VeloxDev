using System.Windows;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WPF.Headless.Tests.Infrastructure;

namespace VeloxDev.WPF.Headless.Tests.Tests;

/// <summary>
/// The WPF drag-in layer: it maps a draft into the decorator's viewport without touching the view pool, and it
/// collapses the draft's world size exactly once, the way a real node card is laid out.
/// </summary>
[STATestClass]
public sealed class NodeDropSurfaceHeadlessTests
{
    private sealed class ProbeSurface : NodeDropSurface
    {
        public NodeDropDrawingContext? Last { get; private set; }
        public int Draws { get; private set; }

        protected override void DrawNode(NodeDropDrawingContext context)
        {
            Last = context;
            Draws++;
        }
    }

    [TestMethod]
    public void MapsTheDraftIntoTheDecoratorsViewport()
    {
        var tree = new TreeDefaultViewModel();
        tree.Layout.Scale = new Scale(2, 2);
        var decorator = new TestGridDecorator
        {
            ScrollOffsetX = 40,
            ScrollOffsetY = 30,
            ContentOffsetX = 10,
            ContentOffsetY = 5,
        };
        var surface = new ProbeSurface { DataContext = tree };
        var draft = new NodeDefaultViewModel
        {
            Anchor = new Anchor(100, 50, 0),
            Size = new VeloxDev.WorkflowSystem.Size(300, 200),
        };

        surface.Begin(draft, decorator);

        var bounds = surface.NodeBounds;
        Assert.IsNotNull(bounds, "a live draft must have viewport bounds");
        Assert.AreEqual(100d - (40d - 10d), bounds.Value.X, 0.001, "worldLeft = scroll − contentOffset");
        Assert.AreEqual(50d - (30d - 5d), bounds.Value.Y, 0.001);
        Assert.AreEqual(150d, bounds.Value.Width, 0.001, "the world size collapses by the tree's scale");
        Assert.AreEqual(100d, bounds.Value.Height, 0.001);
    }

    [TestMethod]
    public void End_ClearsTheDraft()
    {
        var tree = new TreeDefaultViewModel();
        var decorator = new TestGridDecorator();
        var surface = new ProbeSurface { DataContext = tree };
        var draft = new NodeDefaultViewModel { Anchor = new Anchor(10, 20, 0), Size = new VeloxDev.WorkflowSystem.Size(100, 50) };

        surface.Begin(draft, decorator);
        surface.End(draft, decorator, committed: false);

        Assert.IsFalse(surface.IsActive);
        Assert.IsNull(surface.NodeBounds);
    }

    [TestMethod]
    public void BeginDrop_WithoutADecorator_FailsLoudly()
    {
        var surface = new ProbeSurface();
        var drop = WorkflowNodeDrop.For(new TreeDefaultViewModel());

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => surface.BeginDrop(drop, new NodeDefaultViewModel()));

        StringAssert.Contains(error.Message, "decorator");
    }
}
