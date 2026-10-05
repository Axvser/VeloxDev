using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Pins the geometry tools to the behaviour of a hand drag. The two used to differ in ways that only show up
/// when the canvas is zoomed: a link is drawn between two <i>slot</i> anchors, and a node's own
/// <c>Anchor</c> getter is scale-collapsed, so a tool that read it and wrote the sum back as an absolute
/// anchor landed the node at a fraction of the intended distance.
/// </summary>
[TestClass]
public class NodeGeometryToolTests
{
    /// <summary>Two mounted nodes — one to move with the tool, one to move by hand for comparison.</summary>
    private static (TreeDefaultViewModel Tree, WorkflowAgentScope Scope) TwoNodes()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();
        helper.CreateNode(new NodeDefaultViewModel());
        helper.CreateNode(new NodeDefaultViewModel());
        return (tree, new WorkflowAgentScope(tree));
    }

    /// <summary>
    /// The regression this file exists for: at a non-unit scale the tool must land the node exactly where the
    /// drag's own <c>MoveCommand</c> would. The offset is compared as a delta, so the two nodes' differing
    /// creation positions do not matter.
    /// </summary>
    [TestMethod]
    public void MoveNode_LandsWhereADragWould_AtANonUnitScale()
    {
        var (tree, scope) = TwoNodes();
        tree.Layout.Scale = new Scale(0.5, 0.5);

        var beforeTool = tree.Nodes[0].Anchor;
        var beforeDrag = tree.Nodes[1].Anchor;

        WorkflowToolInvoker.Invoke(scope, "MoveNode", ("nodeIndex", 0), ("offsetX", 10.0), ("offsetY", 20.0));
        tree.Nodes[1].MoveCommand.Execute(new Offset(10, 20));

        var viaTool = tree.Nodes[0].Anchor;
        var viaDrag = tree.Nodes[1].Anchor;
        Assert.AreEqual(viaDrag.Horizontal - beforeDrag.Horizontal, viaTool.Horizontal - beforeTool.Horizontal, 0.001,
            "a moved node must travel the same world distance the drag path computes for the same offset");
        Assert.AreEqual(viaDrag.Vertical - beforeDrag.Vertical, viaTool.Vertical - beforeTool.Vertical, 0.001,
            "a moved node must travel the same world distance the drag path computes for the same offset");
    }

    /// <summary>A relative move must not touch z-order — a drag does not.</summary>
    [TestMethod]
    public void MoveNode_KeepsTheNodesLayer()
    {
        var (tree, scope) = TwoNodes();
        var node = tree.Nodes[0];
        node.SetAnchorCommand.Execute(new Anchor(100, 100, 7));

        WorkflowToolInvoker.Invoke(scope, "MoveNode", ("nodeIndex", 0), ("offsetX", 5.0), ("offsetY", 5.0));

        Assert.AreEqual(7, node.Anchor.Layer);
    }

    /// <summary>
    /// An omitted layer keeps the current one. The old default of 0 silently dropped every positioned node to
    /// the bottom of the stack, which reads as a rendering fault rather than as something a tool did.
    /// </summary>
    [TestMethod]
    public void SetNodePosition_WithoutALayerKeepsTheExistingOne()
    {
        var (tree, scope) = TwoNodes();
        var node = tree.Nodes[0];
        node.SetAnchorCommand.Execute(new Anchor(100, 100, 7));

        WorkflowToolInvoker.Invoke(scope, "SetNodePosition", ("nodeIndex", 0), ("left", 200.0), ("top", 300.0));

        Assert.AreEqual(200d, node.Anchor.Horizontal, 0.001);
        Assert.AreEqual(300d, node.Anchor.Vertical, 0.001);
        Assert.AreEqual(7, node.Anchor.Layer, "an omitted layer must not drop the node to z-order 0");
    }

    /// <summary>An explicit layer still replaces it.</summary>
    [TestMethod]
    public void SetNodePosition_WithALayerStillSetsIt()
    {
        var (tree, scope) = TwoNodes();
        var node = tree.Nodes[0];

        WorkflowToolInvoker.Invoke(scope, "SetNodePosition",
            ("nodeIndex", 0), ("left", 10.0), ("top", 10.0), ("layer", 3));

        Assert.AreEqual(3, node.Anchor.Layer);
    }

    /// <summary>
    /// A geometry change re-raises <c>Anchor</c>/<c>Size</c>, which is what makes a platform's slot-layout
    /// behaviour re-measure the slot anchors a link is drawn between. Without it the node card moves and the
    /// line does not until something else forces a layout pass.
    /// </summary>
    [TestMethod]
    public void GeometryTools_ReRaiseAnchorAndSize()
    {
        var (tree, scope) = TwoNodes();
        var node = tree.Nodes[0];
        var raised = new List<string>();
        node.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };

        WorkflowToolInvoker.Invoke(scope, "MoveNode", ("nodeIndex", 0), ("offsetX", 1.0), ("offsetY", 1.0));
        raised.Clear();

        WorkflowToolInvoker.Invoke(scope, "SetNodePosition", ("nodeIndex", 0), ("left", 50.0), ("top", 50.0));
        CollectionAssert.Contains(raised, nameof(IWorkflowNodeViewModel.Anchor));

        raised.Clear();
        WorkflowToolInvoker.Invoke(scope, "ResizeNode", ("nodeIndex", 0), ("width", 321.0), ("height", 123.0));
        CollectionAssert.Contains(raised, nameof(IWorkflowNodeViewModel.Size));
    }

    /// <summary>
    /// What the query tools report is the position the placement tools set — at any zoom.
    /// </summary>
    /// <remarks>
    /// The write side speaks <i>world</i> coordinates (<c>SetAnchorCommand</c> stores straight into the field),
    /// but <c>node.Anchor</c>'s getter is collapsed by the canvas scale for rendering. Reporting the getter
    /// therefore answered a different frame from the one the caller wrote in: placing a node at (3120, 940) and
    /// reading it back gave (2836.36, 854.55), and that ratio moved whenever the user zoomed — so a position
    /// could not be round-tripped and two readings taken at different zooms could not be compared. A hand-run of
    /// the demo hit exactly this and filed it as "the two spaces disagree".
    /// </remarks>
    [TestMethod]
    public void QueryTools_ReportTheWorldPosition_ThePlacementToolsTake()
    {
        var (tree, scope) = TwoNodes();
        tree.Layout.Scale = new Scale(0.5, 0.5);   // view = world / 0.5, so the two frames differ by 2×

        // World coordinates, straight through the command a GUI placement dispatches.
        tree.Nodes[0].SetAnchorCommand.Execute(new Anchor(3120, 940, 0));
        tree.Nodes[0].SetSizeCommand.Execute(new Size(280, 260));

        var detail = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "GetNodeDetail", ("nodeIndex", 0)));
        Assert.AreEqual(3120d, detail["x"]!.Value<double>(), 0.001, "a position must read back as it was written");
        Assert.AreEqual(940d, detail["y"]!.Value<double>(), 0.001);
        Assert.AreEqual(280d, detail["w"]!.Value<double>(), 0.001, "and so must a size");
        Assert.AreEqual(260d, detail["h"]!.Value<double>(), 0.001);

        var listed = JArray.Parse(WorkflowToolInvoker.Invoke(scope, "ListNodes"))
            .Single(n => n!["i"]!.Value<int>() == 0)!;
        Assert.AreEqual(3120d, listed["x"]!.Value<double>(), 0.001, "the list answers in the same frame as the detail");
        Assert.AreEqual(280d, listed["w"]!.Value<double>(), 0.001);
    }

    /// <summary>The round trip an agent actually performs: place by tool, read by tool, get the same numbers.</summary>
    [TestMethod]
    public void SetNodePosition_ReadsBackThroughGetNodeDetail_AtANonUnitScale()
    {
        var (tree, scope) = TwoNodes();
        tree.Layout.Scale = new Scale(2.0, 2.0);

        WorkflowToolInvoker.Invoke(scope, "SetNodePosition",
            ("nodeIndex", 0), ("left", 700.0), ("top", 450.0));

        var detail = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "GetNodeDetail", ("nodeIndex", 0)));
        Assert.AreEqual(700d, detail["x"]!.Value<double>(), 0.001);
        Assert.AreEqual(450d, detail["y"]!.Value<double>(), 0.001);
    }
}
