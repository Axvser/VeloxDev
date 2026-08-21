using Demo.ViewModels;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies fan-out (ParallelEntry) + join wait: one route key points to multiple downstreams, all
/// paths execute, and the join point runs after all branches.
/// </summary>
[TestClass]
public class ParallelFanOutTests
{
    [TestMethod]
    public async Task FanOut_JoinWaitsForAllBranches()
    {
        var (tree, controller, sel, t1, t2, join) = BuildFanOutGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        // Compiled structure: True branch → ParallelEntry (two-way fan-out).
        var branch = graph.Entries.OfType<BranchEntry>().Single();
        var trueOpt = branch.Options.First(o => Equals(o.Key, true));
        var parallel = Assert.IsInstanceOfType<ParallelEntry>(trueOpt.Graph!.Entries[0]);
        Assert.AreEqual(2, parallel.Branches.Count, "True → two fan-out branches");

        sel.Condition = true;
        var context = new RuntimeContext();
        await new CompilerEngine().RunAsync(graph, context, CancellationToken.None);

        Assert.AreEqual("Completed", t1.LastStatus, "T1 ran");
        Assert.AreEqual("Completed", t2.LastStatus, "T2 ran");
        Assert.AreEqual("Completed", join.LastStatus, "Join ran after both fan-out branches (wait semantics)");
    }

    private static (TreeDefaultViewModel Tree, ControllerViewModel Controller, BoolSelectorNodeViewModel Sel,
        NodeViewModel T1, NodeViewModel T2, NodeViewModel Join) BuildFanOutGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var sel = new BoolSelectorNodeViewModel { Title = "FanOut", Condition = true };
        helper.CreateNode(sel);
        var t1 = new NodeViewModel { Title = "T1", DelayMilliseconds = 1 };
        var t2 = new NodeViewModel { Title = "T2", DelayMilliseconds = 1 };
        var join = new NodeViewModel { Title = "Join", DelayMilliseconds = 1 };
        helper.CreateNode(t1); helper.CreateNode(t2); helper.CreateNode(join);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sel.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        t1.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        t2.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        t1.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        t2.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        join.InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);

        Connect(tree, controller.OutputSlot!, sel.InputSlot!);
        Connect(tree, sel.TrueSlot!, t1.InputSlot!);   // fan-out: True → [T1, T2]
        Connect(tree, sel.TrueSlot!, t2.InputSlot!);
        Connect(tree, t1.OutputSlot!, join.InputSlot!);
        Connect(tree, t2.OutputSlot!, join.InputSlot!);

        return (tree, controller, sel, t1, t2, join);
    }

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}
