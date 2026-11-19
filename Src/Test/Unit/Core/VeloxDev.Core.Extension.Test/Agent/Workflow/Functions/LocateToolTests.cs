using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The locate tools are the Agent's only way to ask the host's canvas to show something. They are model-only
/// reads, so what they owe is exactness: the point handed to the surface navigator must be the node's rectangle
/// centre (or the link's midpoint), in the same world frame the placement tools use.
/// </summary>
[TestClass]
public class LocateToolTests
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

    /// <summary>Two nodes, one link, and one navigator registered the way an adapter registers it.</summary>
    private static (TreeDefaultViewModel Tree, WorkflowAgentScope Scope, RecordingNavigator Navigator) Scene()
    {
        var tree = new TreeDefaultViewModel();
        var a = new NodeDefaultViewModel { Parent = tree };
        var b = new NodeDefaultViewModel { Parent = tree };
        tree.Nodes.Add(a);
        tree.Nodes.Add(b);

        a.SetAnchorCommand.Execute(new Anchor(100, 200, 0));
        a.SetSizeCommand.Execute(new Size(80, 40));
        b.SetAnchorCommand.Execute(new Anchor(500, 600, 0));
        b.SetSizeCommand.Execute(new Size(100, 60));

        a.CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneTarget });
        b.CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.OneSource });
        tree.GetHelper().SendConnection(a.Slots[0]);
        tree.GetHelper().ReceiveConnection(b.Slots[0]);
        tree.Links[0].IsVisible = true;

        var navigator = new RecordingNavigator();
        WorkflowInput.For(tree).Navigator = navigator;
        return (tree, new WorkflowAgentScope(tree), navigator);
    }

    [TestMethod]
    public void LocateNode_CentersTheNodesRectangle()
    {
        var (tree, scope, navigator) = Scene();
        try
        {
            var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "LocateNode", ("nodeIndex", 0)));

            Assert.AreEqual("ok", answer["status"]?.Value<string>(), answer.ToString());
            Assert.AreEqual(1, navigator.Calls);
            Assert.AreEqual(140d, navigator.X, 0.001, "anchor 100 + width 80 / 2");
            Assert.AreEqual(220d, navigator.Y, 0.001, "anchor 200 + height 40 / 2");
        }
        finally
        {
            WorkflowInput.For(tree).Navigator = null;
        }
    }

    [TestMethod]
    public void LocateSlot_UsesTheParentNodeCenter()
    {
        var (tree, scope, navigator) = Scene();
        try
        {
            var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "LocateSlot",
                ("nodeIndex", 0), ("slotIndex", 0)));

            Assert.AreEqual("ok", answer["status"]?.Value<string>(), answer.ToString());
            Assert.AreEqual(140d, navigator.X, 0.001);
            Assert.AreEqual(220d, navigator.Y, 0.001);
        }
        finally
        {
            WorkflowInput.For(tree).Navigator = null;
        }
    }

    [TestMethod]
    public void LocateLink_UsesTheEndpointMidpoint()
    {
        var (tree, scope, navigator) = Scene();
        try
        {
            var id = (JArray.Parse(WorkflowToolInvoker.Invoke(scope, "ListConnections")))[0]["id"]!.Value<string>()!;
            var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "LocateLink", ("linkId", id)));

            Assert.AreEqual("ok", answer["status"]?.Value<string>(), answer.ToString());
            Assert.AreEqual(345d, navigator.X, 0.001, "midpoint of 140 and 550");
            Assert.AreEqual(425d, navigator.Y, 0.001, "midpoint of 220 and 630");
        }
        finally
        {
            WorkflowInput.For(tree).Navigator = null;
        }
    }

    [TestMethod]
    public void LocateNode_WithoutANavigator_ReturnsAnError()
    {
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new NodeDefaultViewModel());
        var scope = new WorkflowAgentScope(tree);

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "LocateNode", ("nodeIndex", 0)));

        Assert.AreEqual("error", answer["status"]?.Value<string>(), answer.ToString());
        StringAssert.Contains(answer["message"]?.Value<string>() ?? string.Empty, "could not be located");
    }
}
