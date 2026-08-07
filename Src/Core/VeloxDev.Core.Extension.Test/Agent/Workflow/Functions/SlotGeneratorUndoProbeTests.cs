using Demo.ViewModels;
using Demo.Workflow;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Empirical probe for the "phantom undo/redo entries" audit on dynamic SlotGenerator
/// (SlotEnumerator) nodes.
///
/// Question: when an AI tool operates on a node whose slots are produced by a dynamic
/// SlotEnumerator, does ONE logical operation push MANY undo entries (one per slot)?
///
/// Audit finding (root cause): the AI tools themselves push exactly ONE undo entry per
/// logical op. The real phantom-undo source was <see cref="ObservableCollectionTracker"/>:
/// the generated <c>Items</c> getter re-subscribed a fresh method-group delegate on every
/// access (reference-equality dedup never matched), so each <c>Items.Add</c> fanned out
/// to hundreds of <c>OnItemAddedToItems</c> calls, each firing <c>CreateSlotCommand.Execute</c>
/// — which Submits once the node is attached. Fixed by deduping tracked handlers by
/// (Method, Target) identity instead of delegate reference. This probe is the regression
/// guard: it verifies the contract behaviorally (each tree's own helper, no reflection
/// into the process-global <c>WorkflowTreeEx._cache</c>).
///
/// Contract: CreateNode (mount), selector switch (atomic SetSelector), connection
/// (StandardCreateNewConnection) and cascade delete (atomic StandardDelete) each push
/// exactly ONE undo entry. After each logical operation, exactly ONE <c>Undo()</c> must
/// FULLY reverse it, and ONE <c>Redo()</c> must restore it. If an operation pushed phantom
/// per-slot entries, a single Undo would pop only the top phantom entry and the operation
/// would be only PARTIALLY reversed — the behavioral assertion fails.
/// </summary>
[TestClass]
public class SlotGeneratorUndoProbeTests
{
    [TestMethod]
    public void SlotGeneratorToolOps_OneUndoFullyReversesEachLogicalOp()
    {
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree)
            .WithAutoDiscovery(typeof(EnumSelectorNodeViewModel).Assembly);
        var toolkit = new WorkflowAgentToolkit(scope);

        // 1. CreateNode on the SlotEnumerator node. Its enumerator slots were created during
        //    construction (detached, no Submit); the mount itself must be ONE undoable step.
        //    Note: Tree.CreateNodeCommand.Execute is fire-and-forget, so the returned index can
        //    be -1 until the mount lands — wait for the node type, then resolve the index.
        var create = JObject.Parse(InvokeTool(toolkit, "CreateNode",
            ("fullTypeName", typeof(EnumSelectorNodeViewModel).FullName!)));
        Assert.AreEqual("ok", create["status"]?.Value<string>(), create.ToString());
        int idx = WaitForNode<EnumSelectorNodeViewModel>(tree);
        var createdNode = tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        tree.GetHelper().Undo();
        Assert.IsFalse(tree.Nodes.OfType<EnumSelectorNodeViewModel>().Any(),
            "one Undo fully reverses the mount — the enumerator node is gone");
        tree.GetHelper().Redo();
        Assert.IsTrue(tree.Nodes.OfType<EnumSelectorNodeViewModel>().Any(),
            "one Redo restores the mounted enumerator node");

        // 2. CreateNode on a plain receiver node → one step.
        var target = JObject.Parse(InvokeTool(toolkit, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!)));
        Assert.AreEqual("ok", target["status"]?.Value<string>(), target.ToString());
        int targetIdx = WaitForNode<NodeDefaultViewModel>(tree);
        tree.GetHelper().Undo();
        Assert.IsFalse(tree.Nodes.OfType<NodeDefaultViewModel>().Any(),
            "one Undo fully reverses the receiver-node mount");
        tree.GetHelper().Redo();
        Assert.IsTrue(tree.Nodes.OfType<NodeDefaultViewModel>().Any());

        // 3. Add a receiver slot → one step.
        int slotCountBefore = tree.Nodes[targetIdx].Slots.Count;
        InvokeTool(toolkit, "AddSlotToCollection", ("nodeIndex", targetIdx), ("propertyName", "Slots"),
            ("fullSlotTypeName", typeof(SlotDefaultViewModel).FullName!), ("channel", "OneBoth"));
        WaitForCommandDrain(tree.Nodes[targetIdx].CreateSlotCommand);
        Assert.AreEqual(slotCountBefore + 1, tree.Nodes[targetIdx].Slots.Count);
        tree.GetHelper().Undo();
        Assert.AreEqual(slotCountBefore, tree.Nodes[targetIdx].Slots.Count,
            "one Undo fully reverses the slot add");
        tree.GetHelper().Redo();
        Assert.AreEqual(slotCountBefore + 1, tree.Nodes[targetIdx].Slots.Count);

        // 4. Switch the enumerator to a multi-member enum. SlotChannel has many members — the
        //    strongest phantom-detection case. The switch must be ONE atomic step, not
        //    one CreateSlotCommand/DeleteCommand entry per slot.
        var switcher = (EnumSelectorNodeViewModel)tree.Nodes[idx];
        var sw1 = JObject.Parse(InvokeTool(toolkit, "SetEnumSlotCollection",
            ("nodeIndex", idx), ("propertyName", "OutputSlots"),
            ("selectorTypeOrJson", typeof(SlotChannel).FullName!)));
        Assert.IsTrue(sw1["ok"]?.Value<bool>() ?? false, sw1.ToString());
        Assert.AreEqual(typeof(SlotChannel), switcher.EnumType);
        tree.GetHelper().Undo();
        Assert.AreEqual(typeof(NetworkRequestMethod), switcher.EnumType,
            "one Undo fully reverses the selector switch (atomic) — a per-slot phantom would leave the type un-reverted");
        tree.GetHelper().Redo();
        Assert.AreEqual(typeof(SlotChannel), switcher.EnumType);

        // 5. Connect an enumerator output slot → receiver slot → one step (not send+receive, not
        //    1-per-cleanup).
        var enumSlot = switcher.OutputSlots.Items[0].Slot;
        int senderSlotIndex = switcher.Slots.IndexOf(enumSlot);
        Assert.IsTrue(senderSlotIndex >= 0, "enumerator slot must be registered in node.Slots");
        var conn = JObject.Parse(InvokeTool(toolkit, "ConnectSlots",
            ("senderNodeIndex", idx), ("senderSlotIndex", senderSlotIndex),
            ("receiverNodeIndex", targetIdx), ("receiverSlotIndex", slotCountBefore)));
        Assert.AreEqual("ok", conn["status"]?.Value<string>(), conn.ToString());
        Assert.IsTrue(enumSlot.Targets.Count > 0);
        tree.GetHelper().Undo();
        Assert.AreEqual(0, enumSlot.Targets.Count,
            "one Undo fully reverses the connection — phantom entries would leave the sender still connected");
        tree.GetHelper().Redo();
        Assert.IsTrue(enumSlot.Targets.Count > 0);

        // 6. Switch back to a remembered type (restore + rewire) → one step.
        var sw2 = JObject.Parse(InvokeTool(toolkit, "SetEnumSlotCollection",
            ("nodeIndex", idx), ("propertyName", "OutputSlots"),
            ("selectorTypeOrJson", typeof(NetworkRequestMethod).FullName!)));
        Assert.IsTrue(sw2["ok"]?.Value<bool>() ?? false, sw2.ToString());
        Assert.AreEqual(typeof(NetworkRequestMethod), switcher.EnumType);
        tree.GetHelper().Undo();
        Assert.AreEqual(typeof(SlotChannel), switcher.EnumType,
            "one Undo fully reverses the switch-back (restore + rewire is one atomic step)");
        tree.GetHelper().Redo();
        Assert.AreEqual(typeof(NetworkRequestMethod), switcher.EnumType);

        // 7. DeleteNode cascade (slots + connections) → one atomic step, not 1-per-slot.
        //    (Note: after the switch-back the enumerator is on NetworkRequestMethod, its original
        //    type whose saved state predates any connection — so there is no connection at delete
        //    time here; atomicity is what this asserts: ONE Undo fully restores the node.)
        InvokeTool(toolkit, "DeleteNode", ("nodeIndex", idx));
        Assert.IsFalse(tree.Nodes.OfType<EnumSelectorNodeViewModel>().Any());
        tree.GetHelper().Undo();
        var restored = tree.Nodes.OfType<EnumSelectorNodeViewModel>().SingleOrDefault();
        Assert.IsNotNull(restored,
            "one Undo fully restores the cascade delete — phantom entries would leave the node still deleted");
        Assert.AreEqual(typeof(NetworkRequestMethod), restored!.EnumType,
            "the restored node carries the selector state from before the delete");
        Assert.AreEqual(5, restored.OutputSlots.Items.Count,
            "the restored node carries its enumerator slots (NetworkRequestMethod has 5)");
        tree.GetHelper().Redo();
        Assert.IsFalse(tree.Nodes.OfType<EnumSelectorNodeViewModel>().Any());
    }

    [TestMethod]
    public void DemoSession_BootstrapIsUndoable_StepByStep()
    {
        var session = WorkflowDemoSession.Create();
        var tree = session.Tree;
        var selector = tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();

        // Contract (逐条引导步骤): the default sample is deliberately NOT a clean baseline.
        // Create() keeps the ~62 bootstrap Submits — 15 × CreateNode, the 26 synchronous slot
        // registrations, the 20 connections, and the mounted SetSelector — on the undo stack.
        // Every Submit is synchronous in a deterministic order (the slot factories register via
        // GetHelper().CreateSlot, and SetSelector is atomic), so Ctrl+Z tears the sample apart
        // one setup step at a time and Redo re-builds it. These are intentional bootstrap steps,
        // not phantom entries: each is a real setup operation the user may now undo/redo.

        // Top entry = the mounted SetSelector (WorkflowDemoSession → VoltageRange).
        // VoltageRange is internal to Lib, so resolve it via reflection like the other repros.
        var voltageRange = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");
        Assert.IsNotNull(voltageRange, "VoltageRange should exist in Lib");
        Assert.AreEqual(voltageRange, selector.EnumType,
            "bootstrap mounted the enum selector to VoltageRange");

        // Undo #1 → reverts the selector mount (one step).
        tree.GetHelper().Undo();
        Assert.AreEqual(typeof(NetworkRequestMethod), selector.EnumType,
            "one Undo reverts the last bootstrap step (the mounted SetSelector)");

        // Undo #2 → pops the last bootstrap connection (handleDelete → finalize). Each Connect()
        // is ONE Submit (StandardReceiveConnection → StandardCreateNewConnection); no redo in
        // between, so the connection Submit sits directly under the selector mount.
        int linksBefore = tree.Links.Count;
        Assert.IsGreaterThan(0, linksBefore, "the demo graph carries connections");
        tree.GetHelper().Undo();
        Assert.AreEqual(linksBefore - 1, tree.Links.Count,
            "a second Undo pops the last bootstrap connection (deterministic order)");

        // Redo #1 → re-applies the connection.
        tree.GetHelper().Redo();
        Assert.AreEqual(linksBefore, tree.Links.Count,
            "Redo restores the connection");

        // Redo #2 → re-applies the selector mount.
        tree.GetHelper().Redo();
        Assert.AreEqual(voltageRange, selector.EnumType,
            "a final Redo restores the selector mount");
    }

    /// <summary>
    /// Waits until the node of type <typeparamref name="T"/> is mounted AND the tree's
    /// CreateNodeCommand has fully drained, then returns its index.
    ///
    /// Tree.CreateNodeCommand.Execute is fire-and-forget (VeloxCommand). Under parallel load the
    /// mount can be ENQUEUED (the previous dispatch's ExecuteCoreAsync is still in _active), so
    /// the node appears in tree.Nodes a few microseconds BEFORE its StandardSubmit pushes the
    /// undo entry — a tool-returned "i" is not a reliable commit point. Polling the command's own
    /// (per-instance, not process-global) _active/_pendingQueue until empty means the mount + undo
    /// push are complete: ExecuteCoreAsync only Exits after the Submit.
    /// </summary>
    private static int WaitForNode<T>(IWorkflowTreeViewModel tree) where T : class, IWorkflowNodeViewModel
    {
        for (var i = 0; i < 500 && !tree.Nodes.OfType<T>().Any(); i++)
            Thread.Sleep(10);
        WaitForCommandDrain(tree.CreateNodeCommand);
        var node = tree.Nodes.OfType<T>().SingleOrDefault();
        Assert.IsNotNull(node, $"{typeof(T).Name} node was never mounted");
        return tree.Nodes.IndexOf(node!);
    }

    /// <summary>
    /// Polls a <see cref="VeloxCommand"/>'s per-instance <c>_active</c>/<c>_pendingQueue</c> until
    /// both are empty. Reads race against the background dispatcher, so a collection-mutation
    /// exception is treated as "still busy" and retried. Drain is the boundary after which the
    /// command's Submit (undo push) is guaranteed complete.
    /// </summary>
    private static void WaitForCommandDrain(params IVeloxCommand[] commands)
    {
        var entries = commands
            .Select(c => (
                cmd: c,
                pending: c.GetType().GetField("_pendingQueue", BindingFlags.NonPublic | BindingFlags.Instance),
                active: c.GetType().GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance)))
            .ToArray();
        foreach (var (_, pending, active) in entries)
        {
            Assert.IsNotNull(pending, "VeloxCommand._pendingQueue field not found");
            Assert.IsNotNull(active, "VeloxCommand._active field not found");
        }

        for (var i = 0; i < 500; i++)
        {
            bool drained = true;
            foreach (var (cmd, pendingField, activeField) in entries)
            {
                try
                {
                    var pending = (System.Collections.IEnumerable)pendingField!.GetValue(cmd)!;
                    var active = (System.Collections.IEnumerable)activeField!.GetValue(cmd)!;
                    if (pending.Cast<object?>().Any() || active.Cast<object?>().Any())
                    {
                        drained = false;
                        break;
                    }
                }
                catch (InvalidOperationException)
                {
                    drained = false;   // collection mutated mid-read → still busy, retry
                    break;
                }
            }
            if (drained) return;
            Thread.Sleep(10);
        }

        Assert.Fail("Command queue never drained");
    }

    private static string InvokeTool(WorkflowAgentToolkit toolkit, string toolName, params (string Name, object? Value)[] args)
    {
        var method = typeof(WorkflowAgentToolkit)
            .GetMethod(toolName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, $"Tool method '{toolName}' was not found.");

        var parameters = method.GetParameters();
        var invocationArgs = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                invocationArgs[i] = CancellationToken.None;
                continue;
            }
            var match = args.FirstOrDefault(a => string.Equals(a.Name, parameters[i].Name, System.StringComparison.OrdinalIgnoreCase));
            invocationArgs[i] = match == default ? parameters[i].DefaultValue : match.Value;
        }

        var raw = method.Invoke(toolkit, invocationArgs);
        if (raw is Task<string> asyncResult)
            raw = asyncResult.GetAwaiter().GetResult();
        Assert.IsInstanceOfType<string>(raw);
        return (string)raw!;
    }
}
