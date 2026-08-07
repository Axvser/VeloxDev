using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies the default demo (WorkflowDemoSession.Create(), which switches the enum selector
/// to VoltageRange via line 194) can still wake up downstream handler nodes by routing — the
/// SlotEnumerator preserves the wiring topology across a type switch and defaults the current
/// value to a valid member, so routing keeps working. Also verifies per-credential selection
/// memory across switching.
/// </summary>
[TestClass]
public class DemoEnumSelectorSwitchRepro
{
    [TestMethod]
    public async Task DefaultDemo_RoutesToDownstreamAfterTypeSwitch()
    {
        var session = WorkflowDemoSession.Create();   // default: line 194 → VoltageRange
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();

        // Line 194 switched the selector type after wiring; SlotEnumerator preserves the wiring
        // topology (new branches re-routed onto the old branches' downstream by position).
        int connectedSlots = node.OutputSlots.Items.Count(s => s.Slot.Targets.Count > 0);
        Console.WriteLine($"enum selector type={node.EnumType?.Name} connectedSlots={connectedSlots}");

        var compiler = new WorkflowCompiler();
        var context = NetworkFlowContext.Create("demo");
        var results = compiler.Compile(session.Controller,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Trim);
        await results[0].ExecuteAsync(context, CancellationToken.None);
        int executedHandlers = context.ExecutionTrail.Count(t => t.Contains("Handler"));

        Console.WriteLine($"connectedSlots={connectedSlots} executedHandlers={executedHandlers}");

        // The default demo CAN wake up a downstream handler: SlotEnumerator re-routed the
        // VoltageRange branches onto the old downstream and defaults the current value to a
        // valid member, so routing picks a branch.
        Assert.IsGreaterThan(0, connectedSlots,
            "type switch re-routes the new branches onto the old downstream");
        Assert.IsGreaterThan(0, executedHandlers,
            "the default loaded demo wakes up a downstream handler by routing");
    }

    [TestMethod]
    public void Repro_DemoCredentialValuesPreserved()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        // Now on VoltageRange (B), current=Zero (first member).
        // Select on B, switch to A (NetworkRequestMethod), select, switch back — each credential
        // must remember its own value independently.
        node.SelectedValue = "High";
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Get", node.SelectedValue, "A remembers Get (from demo setup)");
        node.SelectedValue = "Post";
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "B remembers High");
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Post", node.SelectedValue, "A remembers Post");
    }

    [TestMethod]
    public void Repro_UndoRedo_DictRestoration()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");

        // Voltage=High, HTTP=Post — then undo/redo through each credential.
        node.SelectedValue = "High";
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Get", node.SelectedValue, "HTTP restores Get from dict");
        node.SelectedValue = "Post";
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "Voltage restores High from dict");

        // Undo → HTTP: must immediately restore Post from the dict.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual("Post", node.SelectedValue, "undo → HTTP immediately restores Post");
        // Undo → Voltage: High.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual("High", node.SelectedValue, "undo → Voltage immediately restores High");
        // Redo → HTTP: Post.
        session.Tree.GetHelper().Redo();
        Assert.AreEqual("Post", node.SelectedValue, "redo → HTTP immediately restores Post");
        // Redo → Voltage: High.
        session.Tree.GetHelper().Redo();
        Assert.AreEqual("High", node.SelectedValue, "redo → Voltage immediately restores High");
    }

    [TestMethod]
    public void Repro_MethodRouter_UndoSelectSwitch()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");
        Assert.IsNotNull(vr, "VoltageRange should exist in Lib");

        // The demo keeps its bootstrap on the undo stack (逐条引导步骤), but the interactive
        // switches we perform below land on top of it — so a single Undo pops exactly our own
        // switch, deterministically. To exercise the undo path, perform switches of our own, then
        // undo one.
        // The demo starts on VoltageRange; select a Voltage value first so "retain previous
        // selection" is meaningful.
        node.SelectedValue = "High";

        // Switch to HTTP → HTTP must show GET (the value the demo's initial setup selected).
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual(typeof(NetworkRequestMethod), node.EnumType);
        Assert.AreEqual("Get", node.SelectedValue, "HTTP shows GET selected");

        // Select an HTTP mode, then switch back to Voltage — Voltage must retain High.
        node.SelectedValue = "Post";
        Assert.AreEqual("Post", node.SelectedValue);
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "Voltage retains its previous selection");

        // Undo the Voltage switch → back to HTTP; HTTP must retain Post.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual(typeof(NetworkRequestMethod), node.EnumType, "undo returns to HTTP");
        Assert.AreEqual("Post", node.SelectedValue, "undo to HTTP shows POST selected");
    }

    [TestMethod]
    public async Task DefaultDemo_AfterUndoSelectSwitch_RoutesToHandler()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();

        // The demo keeps its bootstrap on the undo stack, so an interactive switch performed on
        // top is the deterministic top entry. Exercise the switch/undo path here: switch the
        // selector, then undo it — the remembered state (type + routing connections) must
        // restore, and routing must still wake up a downstream handler.
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        session.Tree.GetHelper().Undo();   // → back to the pre-switch type (VoltageRange) + connections
        int connectedSlots = node.OutputSlots.Items.Count(s => s.Slot.Targets.Count > 0);

        var compiler = new WorkflowCompiler();
        var context = NetworkFlowContext.Create("demo");
        var results = compiler.Compile(session.Controller,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Trim);
        await results[0].ExecuteAsync(context, CancellationToken.None);
        int executedHandlers = context.ExecutionTrail.Count(t => t.Contains("Handler"));

        Console.WriteLine($"after undo select-switch: type={node.EnumType?.Name} " +
                          $"connectedSlots={connectedSlots} executedHandlers={executedHandlers}");
        Assert.IsGreaterThan(0, connectedSlots, "undoing a selector switch restores the routing connections");
        Assert.IsGreaterThan(0, executedHandlers,
            "with connections restored, routing wakes up a downstream handler");
    }
}
