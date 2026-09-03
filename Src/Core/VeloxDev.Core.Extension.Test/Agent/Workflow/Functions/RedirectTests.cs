using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies IRedirectable in-chain redirects: a RedirectGate redirects to the chain head for the first
/// FailCount runs, then passes; exceeding the redirect cap throws.
/// </summary>
[TestClass]
public class RedirectTests
{
    [TestMethod]
    public async Task RedirectGate_RedirectsToChainHead_ThenSucceeds()
    {
        var (tree, graph, gate, work, sink) = BuildRedirectGraph();
        gate.FailCount = 2;

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        Assert.AreEqual(3, context.Attempt, "3 chain passes (2 redirects + 1 success)");
        Assert.AreEqual("Completed", work.LastStatus, "Work ran");
        Assert.AreEqual("Completed", sink.LastStatus, "Sink ran after the gate stopped redirecting");
        Assert.IsTrue(context.Logs.Any(l => l.Contains("Redirecting")), "redirects logged by the engine");
        Assert.IsTrue(gate.RuntimeContext is not null, "IRuntimeAware injection still works");
    }

    [TestMethod]
    public async Task RedirectGate_ExceedsCap_Throws()
    {
        var (_, graph, gate, _, _) = BuildRedirectGraph();
        gate.FailCount = 99;   // always redirects → hits the engine's redirect cap

        var context = new RuntimeContext();
        Exception? caught = null;
        try
        {
            await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);
        }
        catch (Exception ex) { caught = ex; }

        Assert.IsInstanceOfType<InvalidOperationException>(caught, "redirect cap exceeded → throws");
        Assert.IsTrue(context.Logs.Any(l => l.Contains("Aborting")), "cap exhaustion logged");
    }

    [TestMethod]
    public async Task WarnWithoutRedirect_EndsFlow_StatusMinusOne()
    {
        var (tree, controller, warn, sink) = BuildWarnOnlyGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        Assert.AreEqual(-1, context.CurrentOrder, "flow ended at the standard -1 stop state");
        Assert.AreEqual("Stopped", context.Status, "run status reflects the stopped end");
        Assert.AreEqual("Idle", sink.LastStatus, "Sink never ran — the flow ended at the warn node");
    }

    [TestMethod]
    public async Task RedirectCrossChain_SkipsPriorAndReruns()
    {
        // Graph: Controller → Pre → Bool(True → [B, Gate, Sink]). Gate redirects back 3 steps = Pre (the previous ChainSegment).
        var (_, graph, gate, _, sink) = BuildBranchRedirectGraph(redirectBackSteps: 3, failCount: 1);

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        Assert.AreEqual(2, context.Attempt, "two graph passes (one redirect across chains)");
        Assert.AreEqual("Completed", sink.LastStatus, "flow recovered after the cross-chain fall back");
        Assert.IsTrue(context.Logs.Any(l => l.Contains("compile state #1")),
            "redirect crossed into the prior chain (Pre, Order #1)");
    }

    [TestMethod]
    public async Task RedirectToRouter_ReroutesWithoutRecompute()
    {
        // Graph as above. Gate redirects back 2 steps = BoolSelector(router)'s Order.
        // On the re-run the router only re-routes, without recomputing (ReceiveAsync is not driven).
        var (_, graph, _, _, sink) = BuildBranchRedirectGraph(redirectBackSteps: 2, failCount: 1);

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        Assert.AreEqual(2, context.Attempt, "two graph passes");
        Assert.AreEqual(1, context.Logs.Count(l => l.Contains("BoolSelectorNodeViewModel")),
            "router driven exactly once — re-routed on the re-run, not recomputed");
        Assert.AreEqual("Completed", sink.LastStatus, "flow recovered after re-routing at the router");
        Assert.IsTrue(context.Logs.Any(l => l.Contains("compile state #2")),
            "redirect targeted the router's compile state (Order #2)");
    }

    private static (TreeDefaultViewModel Tree, CompiledGraph Graph, RedirectGateNodeViewModel Gate,
        NodeViewModel Pre, NodeViewModel Sink) BuildBranchRedirectGraph(int redirectBackSteps, int failCount)
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var pre = new NodeViewModel { Title = "Pre", DelayMilliseconds = 1 };
        helper.CreateNode(pre);
        var sel = new BoolSelectorNodeViewModel { Title = "Router", Condition = true };
        helper.CreateNode(sel);
        var b = new NodeViewModel { Title = "B", DelayMilliseconds = 1 };
        helper.CreateNode(b);
        var gate = new RedirectGateNodeViewModel
        {
            Title = "Gate", DelayMilliseconds = 1,
            FailCount = failCount, RedirectBackSteps = redirectBackSteps,
        };
        helper.CreateNode(gate);
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(sink);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        pre.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        pre.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sel.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        b.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        b.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        gate.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        gate.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, controller.OutputSlot!, pre.InputSlot!);
        Connect(tree, pre.OutputSlot!, sel.InputSlot!);
        Connect(tree, sel.TrueSlot!, b.InputSlot!);
        Connect(tree, b.OutputSlot!, gate.InputSlot!);
        Connect(tree, gate.OutputSlot!, sink.InputSlot!);

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        return (tree, compiler.Graphs[0], gate, pre, sink);
    }

    [TestMethod]
    public async Task ThrowWithoutRedirect_EndsFlow_StatusMinusOne()
    {
        var (tree, controller, throwNode, sink) = BuildThrowOnlyGraph();

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        var graph = compiler.Graphs[0];

        var context = new RuntimeContext();
        Exception? caught = null;
        try
        {
            await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);
        }
        catch (Exception ex) { caught = ex; }

        Assert.IsNull(caught, "a thrown node exception is handled as a redirect signal, not rethrown");
        Assert.AreEqual(-1, context.CurrentOrder, "flow ended at the standard -1 stop state");
        Assert.AreEqual("Stopped", context.Status, "run status reflects the stopped end");
        Assert.AreEqual("Idle", sink.LastStatus, "Sink never ran — the flow ended at the throwing node");
    }

    private static (TreeDefaultViewModel Tree, ControllerViewModel Controller, ThrowOnlyNodeViewModel Throw, NodeViewModel Sink) BuildThrowOnlyGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var throwNode = new ThrowOnlyNodeViewModel { Title = "Throw", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(throwNode); helper.CreateNode(sink);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        throwNode.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        throwNode.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, controller.OutputSlot!, throwNode.InputSlot!);
        Connect(tree, throwNode.OutputSlot!, sink.InputSlot!);

        return (tree, controller, throwNode, sink);
    }

    private static (TreeDefaultViewModel Tree, ControllerViewModel Controller, WarnOnlyNodeViewModel Warn, NodeViewModel Sink) BuildWarnOnlyGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var warn = new WarnOnlyNodeViewModel { Title = "Warn", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(warn); helper.CreateNode(sink);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        warn.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        warn.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, controller.OutputSlot!, warn.InputSlot!);
        Connect(tree, warn.OutputSlot!, sink.InputSlot!);

        return (tree, controller, warn, sink);
    }

    private static (TreeDefaultViewModel Tree, CompiledGraph Graph, RedirectGateNodeViewModel Gate,
        NodeViewModel Work, NodeViewModel Sink) BuildRedirectGraph()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();

        var controller = new ControllerViewModel();
        helper.CreateNode(controller);
        var gate = new RedirectGateNodeViewModel { Title = "Redirect Gate", DelayMilliseconds = 1 };
        var work = new NodeViewModel { Title = "Work", DelayMilliseconds = 1 };
        var sink = new NodeViewModel { Title = "Sink", DelayMilliseconds = 1 };
        helper.CreateNode(gate); helper.CreateNode(work); helper.CreateNode(sink);

        controller.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        gate.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        gate.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        work.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        work.OutputSlot.SetChannelCommand.Execute(SlotChannel.OneTarget);
        sink.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);

        Connect(tree, controller.OutputSlot!, gate.InputSlot!);
        Connect(tree, gate.OutputSlot!, work.InputSlot!);
        Connect(tree, work.OutputSlot!, sink.InputSlot!);

        var compiler = new CompilerViewModel();
        compiler.CompileAsync(controller).GetAwaiter().GetResult();
        return (tree, compiler.Graphs[0], gate, work, sink);
    }

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}

/// <summary>Test node: calls Warn() to request a redirect but does not implement IRedirectable → the flow should end with status -1.</summary>
[WorkflowBuilder.Node<WarnOnlyHelper>(workSemaphore: 1)]
public partial class WarnOnlyNodeViewModel : NodeViewModel { }

public class WarnOnlyHelper : HttpHelper<NodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (ctx is IRuntimeContext rt && rt.Attempt <= 1)
            rt.Warn("Simulated failure: redirect not supported");
        return base.ReceiveAsync(ctx, ct);
    }
}

/// <summary>Test node: ReceiveAsync throws (does not implement IRedirectable) → the flow should end with status -1.</summary>
[WorkflowBuilder.Node<ThrowOnlyHelper>(workSemaphore: 1)]
public partial class ThrowOnlyNodeViewModel : NodeViewModel { }

public class ThrowOnlyHelper : HttpHelper<NodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (ctx is IRuntimeContext rt && rt.Attempt <= 1)
            throw new InvalidOperationException("Simulated failure: threw an exception and redirect not supported");
        return base.ReceiveAsync(ctx, ct);
    }
}
