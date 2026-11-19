using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Locating a component is a request to the surface, not a mutation of the model: the extension computes the
/// world point the component occupies and hands it to whatever navigator an adapter registered. These tests pin
/// the point each component kind reports, and the answers when no surface is attached.
/// </summary>
[TestClass]
public class WorkflowNavigationExTests
{
    private sealed class RecordingNavigator : IWorkflowSurfaceNavigator
    {
        public int Calls { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }

        public void CenterOn(double worldX, double worldY)
        {
            Calls++;
            X = worldX;
            Y = worldY;
        }
    }

    private static TreeDefaultViewModel TreeWithNavigator(out RecordingNavigator navigator)
    {
        var tree = new TreeDefaultViewModel();
        navigator = new RecordingNavigator();
        WorkflowInput.For(tree).Navigator = navigator;
        return tree;
    }

    private static NodeDefaultViewModel Node(TreeDefaultViewModel tree, double left, double top, double width = 40d, double height = 20d)
        => new()
        {
            Parent = tree,
            Anchor = new Anchor(left, top, 0),
            Size = new Size { Width = width, Height = height },
        };

    [TestMethod]
    public void ANodeCentersOnItsRectangle()
    {
        var tree = TreeWithNavigator(out var navigator);
        var node = Node(tree, 100, 200);
        tree.Nodes.Add(node);

        Assert.IsTrue(node.Locate());

        Assert.AreEqual(1, navigator.Calls);
        Assert.AreEqual(120d, navigator.X);
        Assert.AreEqual(210d, navigator.Y);
    }

    [TestMethod]
    public void ASlotCentersOnItsParentNodesRectangle()
    {
        var tree = TreeWithNavigator(out var navigator);
        var node = Node(tree, 100, 200);
        var slot = new SlotDefaultViewModel { Parent = node, Anchor = new Anchor(110, 205, 0) };
        node.Slots.Add(slot);

        Assert.IsTrue(slot.Locate());

        Assert.AreEqual(1, navigator.Calls);
        Assert.AreEqual(120d, navigator.X, "the slot's own anchor is deliberately ignored");
        Assert.AreEqual(210d, navigator.Y);
    }

    [TestMethod]
    public void ALinkCentersBetweenItsEndpointNodes_NotThePublishedCurve()
    {
        var tree = TreeWithNavigator(out var navigator);
        var senderNode = Node(tree, 0, 0);        // center (20, 10)
        var receiverNode = Node(tree, 300, 300);  // center (320, 310)
        var sender = new SlotDefaultViewModel { Parent = senderNode, Anchor = new Anchor(0, 0, 0) };
        var receiver = new SlotDefaultViewModel { Parent = receiverNode, Anchor = new Anchor(100, 100, 0) };
        var link = new LinkDefaultViewModel { Sender = sender, Receiver = receiver };
        ((ILinkHitTestable)link.GetHelper()).SetCurve(
            LinkCurve.FromPoints([0d, 100d], [0d, 100d]));

        Assert.IsTrue(link.Locate());

        Assert.AreEqual(1, navigator.Calls);
        Assert.AreEqual(170d, navigator.X, "the curve and the slot anchors are deliberately ignored");
        Assert.AreEqual(160d, navigator.Y);
    }

    [TestMethod]
    public void WithoutANavigatorNothingMoves()
    {
        var tree = new TreeDefaultViewModel();
        var node = Node(tree, 100, 200);

        Assert.IsFalse(node.Locate());
    }

    [TestMethod]
    public void AnUnmeasuredSlotStillCentersOnItsParentNode()
    {
        var tree = TreeWithNavigator(out var navigator);
        var node = Node(tree, 100, 200);
        var slot = new SlotDefaultViewModel { Parent = node };   // anchor still NaN

        Assert.IsTrue(slot.Locate());

        Assert.AreEqual(1, navigator.Calls);
        Assert.AreEqual(120d, navigator.X);
        Assert.AreEqual(210d, navigator.Y);
    }
}
