using System.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// The drag-in gesture: setting a draft activates it, pointer moves write the world position into the draft,
/// releasing over the canvas creates it through the tree's own command, and leaving without releasing cancels
/// it. The surface is told what happened at each step, exactly once.
/// </summary>
[TestClass]
public class WorkflowNodeDropTests : WorkflowInputTestBase
{
    private sealed class RecordingSurface : IWorkflowNodeDropSurface<string>
    {
        public List<string> Calls { get; } = [];
        public IWorkflowNodeViewModel? Last { get; private set; }
        public string? Context { get; private set; }

        public void Begin(IWorkflowNodeViewModel node, string context)
        {
            Calls.Add("begin");
            Last = node;
            Context = context;
        }

        public void Move(IWorkflowNodeViewModel node, string context)
        {
            Calls.Add("move");
            Last = node;
        }

        public void End(IWorkflowNodeViewModel node, string context, bool committed)
        {
            Calls.Add(committed ? "end:true" : "end:false");
            Last = node;
        }
    }

    private static void WaitForCreate(TreeDefaultViewModel tree, IWorkflowNodeViewModel node)
        => Assert.IsTrue(
            SpinWait.SpinUntil(() => tree.Nodes.Contains(node), TimeSpan.FromSeconds(5)),
            "the tree's create command never ran");

    [TestMethod]
    public void Begin_ActivatesTheDraftAndTellsTheSurface()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel { Anchor = new Anchor(0, 0, 7) };
        var surface = new RecordingSurface();
        var drop = WorkflowNodeDrop.For(tree);
        var changes = 0;
        drop.Changed += (_, _) => changes++;

        drop.Begin(node, surface, "canvas", new Anchor(10, 20, 0));

        Assert.AreEqual(1, changes, "the flag changing is observable without polling");
        Assert.AreEqual(10d, drop.Pointer!.Horizontal, 0.001);
        Assert.IsTrue(drop.IsActive);
        Assert.AreSame(node, drop.PendingNode);
        Assert.AreEqual(10d, node.Anchor.Horizontal, 0.001);
        Assert.AreEqual(20d, node.Anchor.Vertical, 0.001);
        Assert.AreEqual(7, node.Anchor.Layer, "the draft's own layer survives the pointer write");
        CollectionAssert.AreEqual(new[] { "begin" }, surface.Calls);
        Assert.AreEqual("canvas", surface.Context);
    }

    [TestMethod]
    public void PointerMove_SyncsTheDraftAndTheSurface()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        var surface = new RecordingSurface();
        var drop = WorkflowNodeDrop.For(tree);
        drop.Begin(node, surface, "canvas");
        var input = WorkflowInput.For(tree);

        input.Route(Move(100, 200));

        Assert.AreEqual(100d, node.Anchor.Horizontal, 0.001);
        Assert.AreEqual(200d, node.Anchor.Vertical, 0.001);
        CollectionAssert.AreEqual(new[] { "begin", "move" }, surface.Calls);
    }

    [TestMethod]
    public void PointerRelease_CreatesTheNodeAtTheReleasePoint()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        var surface = new RecordingSurface();
        var drop = WorkflowNodeDrop.For(tree);
        drop.Begin(node, surface, "canvas");
        var input = WorkflowInput.For(tree);

        input.Route(Move(100, 200));
        input.Route(Release(300, 400));

        Assert.IsFalse(drop.IsActive);
        Assert.IsNull(drop.PendingNode);
        Assert.AreEqual(300d, node.Anchor.Horizontal, 0.001);
        Assert.AreEqual(400d, node.Anchor.Vertical, 0.001);
        Assert.AreEqual(300d, drop.Pointer!.Horizontal, 0.001, "the last drop point stays readable");
        Assert.AreEqual("end:true", surface.Calls[^1]);
        Assert.IsFalse(surface.Calls.Contains("end:false"), "a committed drag is not also cancelled");
        WaitForCreate(tree, node);
    }

    [TestMethod]
    public void PointerExit_CancelsWithoutCreating()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        var surface = new RecordingSurface();
        var drop = WorkflowNodeDrop.For(tree);
        drop.Begin(node, surface, "canvas");
        var input = WorkflowInput.For(tree);

        input.Route(Move(100, 200));
        input.Route(Exit(500, 500));

        Assert.IsFalse(drop.IsActive);
        Assert.AreEqual("end:false", surface.Calls[^1]);
        Assert.IsFalse(tree.Nodes.Contains(node), "cancelling must not add the draft");
        Assert.IsNull(node.Parent);
    }

    [TestMethod]
    public void AReleaseTheHostVetoes_CancelsInsteadOfCreating()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        var drop = WorkflowNodeDrop.For(tree);
        drop.Begin(node);
        var input = WorkflowInput.For(tree);
        Events(tree).Input.PointerReleased += (_, e) => e.Handle.PreventDefault = true;

        input.Route(Release(300, 400));

        Assert.IsFalse(drop.IsActive, "a vetoed drop must not leave a preview behind");
        Assert.IsFalse(tree.Nodes.Contains(node));
    }

    [TestMethod]
    public void BeginningASecondDraft_CancelsTheFirst()
    {
        var tree = new TreeDefaultViewModel();
        var first = new NodeDefaultViewModel();
        var second = new NodeDefaultViewModel();
        var surface = new RecordingSurface();
        var drop = WorkflowNodeDrop.For(tree);
        drop.Begin(first, surface, "canvas");

        drop.Begin(second, surface, "canvas");

        Assert.AreSame(second, drop.PendingNode);
        CollectionAssert.AreEqual(new[] { "begin", "end:false", "begin" }, surface.Calls);
        Assert.IsNull(first.Parent);
    }

    [TestMethod]
    public void Begin_RejectsANodeThatAlreadyBelongsToATree()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel { Parent = tree };

        var error = Assert.ThrowsExactly<ArgumentException>(() => WorkflowNodeDrop.For(tree).Begin(node));

        StringAssert.Contains(error.Message, "must not belong to a tree");
    }

    [TestMethod]
    public void MovingWithoutADraft_DoesNothing()
    {
        var drop = WorkflowNodeDrop.For(new TreeDefaultViewModel());

        drop.MoveTo(new Anchor(10, 20, 0));
        drop.Commit();
        drop.Cancel();

        Assert.IsFalse(drop.IsActive);
    }
}
