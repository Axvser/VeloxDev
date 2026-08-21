using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies plain-node fan-out (a non-router node whose output fans out to several downstreams) — the compiler
/// must turn it into a ParallelEntry and continue from the common join point, instead of stopping the walk
/// at the fan-out source (previously only routers could fan out). Regression for the desktop JSON example's
/// "compile doesn't walk fully" report.
/// </summary>
[TestClass]
public class PlainNodeFanOutTests
{
    [TestMethod]
    public async Task PlainNode_MultipleTargets_CompilesToParallelAndJoin()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();
        var controller = new ControllerViewModel { Anchor = new Anchor(60, 60, 0), Size = new Size(220, 340) };
        var a = new NodeViewModel { Title = "A", Anchor = new Anchor(320, 60, 0), Size = new Size(220, 160) };
        var b = new NodeViewModel { Title = "B", Anchor = new Anchor(620, 20, 0), Size = new Size(220, 160) };
        var c = new NodeViewModel { Title = "C", Anchor = new Anchor(620, 180, 0), Size = new Size(220, 160) };
        var d = new NodeViewModel { Title = "D", Anchor = new Anchor(920, 60, 0), Size = new Size(220, 160) };

        foreach (var n in new IWorkflowNodeViewModel[] { controller, a, b, c, d })
            helper.CreateNode(n);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        a.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        a.OutputSlot.SetChannelCommand.Execute(SlotChannel.MultipleTargets);
        b.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        b.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        c.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        c.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        d.InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);

        Connect(tree, controller.OutputSlot!, a.InputSlot!);
        Connect(tree, a.OutputSlot!, b.InputSlot!);
        Connect(tree, a.OutputSlot!, c.InputSlot!);
        Connect(tree, b.OutputSlot!, d.InputSlot!);
        Connect(tree, c.OutputSlot!, d.InputSlot!);

        var compiler = new CompilerViewModel();
        var graphs = await compiler.CompileAsync(controller);
        Assert.AreEqual(1, graphs.Count, "single controller → one compiled graph");
        var graph = graphs[0];

        Assert.IsTrue(graph.Entries.Any(e => e is ParallelEntry),
            "plain-node fan-out must compile to a ParallelEntry (not stop the walk at the fan-out source)");
        Assert.IsTrue(graph.Entries.Any(e => e is ExecuteEntry),
            "the join node still compiles as a linear segment after the fan-out");

        foreach (var n in new IWorkflowNodeViewModel[] { controller, a, b, c, d })
        {
            var cc = (n as ICompileTimeAware)?.CompileContext;
            Assert.IsTrue(cc?.Order >= 0, $"{n} must be compiled (order >= 0), not dropped");
        }

        var joinInputs = (d as ICompileTimeAware)?.CompileContext?.InputNodes;
        Assert.AreEqual(2, joinInputs?.Count, "the join node registers both fan-out branches as its inputs");
    }

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}
