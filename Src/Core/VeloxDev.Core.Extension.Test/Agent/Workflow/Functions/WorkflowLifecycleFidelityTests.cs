using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies the Agent toolkit honors the framework's lifecycle contract:
/// tools must replay the same backend call sequence a human interaction triggers,
/// so state (node.Slots, undo history) stays consistent.
/// </summary>
[TestClass]
public class WorkflowLifecycleFidelityTests
{
    /// <summary>
    /// Invokes a workflow tool through its PUBLIC registration path (scope.ProvideTools() →
    /// AIFunction.InvokeAsync), the same route an AI host uses — not by reflecting into private methods.
    /// Arguments are bound by parameter name; null values are omitted so the method's defaults apply.
    /// </summary>
    private static string InvokeTool(WorkflowAgentScope scope, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = scope.ProvideTools()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)) as AIFunction
            ?? throw new InvalidOperationException($"Tool '{toolName}' was not registered.");

        var aiArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null)
                aiArgs[name] = value;

        var result = tool.InvokeAsync(aiArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result switch
        {
            string s => s,
            JsonElement je => je.GetString() ?? string.Empty,
            _ => result?.ToString() ?? string.Empty,
        };
    }

    [TestMethod]
    public void AddSlotToCollection_RegistersSlotInNodeSlots_AndIsUndoable()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node); // synchronous mount (helper path, mirrors CreateNodeCommand)
        var scope = new WorkflowAgentScope(tree);

        // "Slots" is detected as a slot-collection property, so this exercises the
        // collection-slot registration path directly against the canonical collection.
        var result = InvokeTool(scope, "AddSlotToCollection",
            ("nodeIndex", 0),
            ("propertyName", "Slots"),
            ("fullSlotTypeName", typeof(SlotDefaultViewModel).FullName!),
            ("channel", "OneBoth"));

        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>());

        // Slot must be registered exactly once in node.Slots (canonical graph state) with Parent set.
        Assert.HasCount(1, node.Slots, "slot should be registered in node.Slots");
        var slot = node.Slots[0];
        Assert.IsTrue(ReferenceEquals(slot.Parent, node), "slot.Parent should point to the node");

        // Undo once removes it from node.Slots AND unmounts it (atomic lifecycle reversal).
        tree.GetHelper().Undo();
        Assert.IsEmpty(node.Slots, "undo should remove the slot from node.Slots");
        Assert.IsNull(slot.Parent, "undo should unmount the slot");

        // Redo restores the same instance.
        tree.GetHelper().Redo();
        Assert.HasCount(1, node.Slots, "redo should restore the slot");
        Assert.IsTrue(ReferenceEquals(node.Slots[0], slot), "redo should restore the same slot instance");
        Assert.IsTrue(ReferenceEquals(slot.Parent, node));
    }

    [TestMethod]
    public void ExecuteNode_WaitsForCommandCompletion()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node); // synchronous mount
        var scope = new WorkflowAgentScope(tree).WithAllowNodeExecution(true);

        var result = InvokeTool(scope, "ExecuteNode", ("nodeIndex", 0));

        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>());
        Assert.IsTrue((json["message"]?.Value<string>() ?? string.Empty).Contains("completed", StringComparison.OrdinalIgnoreCase),
            "ExecuteNode should report actual completion, not mere dispatch");
    }

    [TestMethod]
    public void SlotEnumerator_RoundTripsSelectorType()
    {
        var node = new NodeDefaultViewModel();
        var enumerator = new SlotEnumerator<SlotDefaultViewModel>();
        enumerator.Install(node, "OutputSlots");
        enumerator.SetSelector(typeof(SlotChannel));

        var holder = new SlotEnumeratorHolder { Enumerator = enumerator };
        var json = holder.Serialize();
        var restored = json.Deserialize<SlotEnumeratorHolder>();

        Assert.IsNotNull(restored.Enumerator, "deserialized holder should contain the enumerator");
        Assert.AreEqual(typeof(SlotChannel).FullName, restored.Enumerator!.SelectorTypeName,
            "SelectorTypeName must survive the round-trip");
        Assert.AreEqual(typeof(SlotChannel), restored.Enumerator.SelectorType,
            "SelectorType must survive the round-trip (re-resolved from SelectorTypeName)");
    }

    [TestMethod]
    public async Task MoveNode_ReplaysGuiDragSemantics_AndIsNonUndoable()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        node.Anchor = new Anchor(10, 20, 0);
        var scope = new WorkflowAgentScope(tree);

        var result = InvokeTool(scope, "MoveNode", ("nodeIndex", 0), ("offsetX", 50), ("offsetY", 30));
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>());

        // MoveNode dispatches SetAnchorCommand — the exact command every GUI drag adapter fires per
        // delta (MoveCommand/SetAnchorCommand → Helper.SetAnchor → StandardSetAnchor, no Submit).
        // Lifecycle fidelity therefore means the move is NOT undoable, exactly like dragging a node
        // by hand: Core's undo history only records commands that Submit (CreateSlot, Delete,
        // connection, SetSelector). Asserting "one-step undo" would claim a contract Core does not have.
        await WaitUntilAsync(() => node.Anchor.Horizontal == 60 && node.Anchor.Vertical == 50,
            "MoveNode should move the node");

        tree.GetHelper().Undo();
        Assert.IsTrue(node.Anchor.Horizontal == 60 && node.Anchor.Vertical == 50,
            "MoveNode is non-undoable (matches GUI drag semantics): Undo must not nudge the anchor");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                Assert.Fail($"Timed out waiting for: {message}");
            await Task.Delay(5).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void WithSynchronizationContext_MarshalsToolCallsToContext()
    {
        var tree = new TreeDefaultViewModel();
        var sync = new TestSynchronizationContext();
        var scope = new WorkflowAgentScope(tree)
            .WithSynchronizationContext(sync)
            .WithAutoMarkDirty(true);
        var tools = scope.ProvideTools();
        var markDirty = tools.First(t => string.Equals(t.Name, "MarkDirty", StringComparison.OrdinalIgnoreCase));

        // Sync-block so Post's counter is written AND read on the same thread — deterministic
        // regardless of how the async continuations are scheduled.
        var result = ((AIFunction)markDirty)
            .InvokeAsync(new AIFunctionArguments(), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        var resultText = result switch
        {
            string s => s,
            JsonElement je => je.GetString() ?? string.Empty,
            _ => result?.ToString() ?? string.Empty,
        };
        var markDirtyResult = JObject.Parse(resultText);
        Assert.AreEqual("ok", markDirtyResult["status"]?.Value<string>(), "tool should have executed");

        // The marshal branch runs the tool via Post on the configured context instance.
        var syncCount = sync.PostCount;
        Assert.AreEqual(1, syncCount, $"tool call should have been marshalled once through the UI context (PostCount={syncCount})");
    }

    private sealed class SlotEnumeratorHolder : System.ComponentModel.INotifyPropertyChanged
    {
        public SlotEnumerator<SlotDefaultViewModel>? Enumerator { get; set; }
#pragma warning disable CS0067 // event never used; required only to satisfy INotifyPropertyChanged for serialization
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
    }

    private sealed class TestSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            d(state); // run inline for deterministic test
        }
    }
}
