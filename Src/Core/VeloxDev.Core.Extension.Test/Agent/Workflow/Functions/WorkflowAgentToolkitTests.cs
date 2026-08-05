using Demo.ViewModels;
using Demo.Workflow;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

[TestClass]
public class WorkflowAgentToolkitTests
{
    [TestMethod]
    public void MarkDirty_MarksTreeDirty()
    {
        var tree = new TreeDefaultViewModel();
        var helper = new TestTreeHelper();
        tree.SetHelper(helper);
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree));

        var result = InvokeTool(toolkit, "MarkDirty");
        var json = JObject.Parse(result);

        Assert.AreEqual("ok", json["status"]?.Value<string>());
        Assert.AreEqual(1, helper.MarkDirtyCount);
    }

    [TestMethod]
    public void QueryTools_DoNotMarkDirty_WhenAutoMarkDirtyEnabled()
    {
        var tree = new TreeDefaultViewModel();
        var helper = new TestTreeHelper();
        tree.SetHelper(helper);
        var scope = new WorkflowAgentScope(tree).WithAutoMarkDirty(true);
        var toolkit = new WorkflowAgentToolkit(scope);

        // Pure read tools must never dirty the tree, even when AutoMarkDirty is on.
        InvokeTool(toolkit, "ListConnections");
        InvokeTool(toolkit, "GetTypeSchema", ("fullTypeName", "System.String"));
        Assert.AreEqual(0, helper.MarkDirtyCount);
    }

    [TestMethod]
    public void CreateTools_QueryOnly_ExcludesMutationAndLayoutTools()
    {
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(new TreeDefaultViewModel()));

        var query = toolkit.CreateTools(WorkflowToolCategory.Query);
        Assert.IsTrue(query.Any(t => string.Equals(t.Name, "ListNodes", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(query.Any(t => string.Equals(t.Name, "GetFullTopology", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(query.Any(t => string.Equals(t.Name, "CreateNode", StringComparison.OrdinalIgnoreCase)),
            "CreateNode is a mutation tool and must not appear in a Query-only set");
        Assert.IsFalse(query.Any(t => string.Equals(t.Name, "ExecuteWork", StringComparison.OrdinalIgnoreCase)),
            "ExecuteWork is an execution tool and must not appear in a Query-only set");
        Assert.IsFalse(query.Any(t => string.Equals(t.Name, "AutoLayout", StringComparison.OrdinalIgnoreCase)),
            "AutoLayout is a layout tool and must not appear in a Query-only set");
    }

    [TestMethod]
    public void CreateTools_All_MatchesDefaultAndIncludesEveryCategory()
    {
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(new TreeDefaultViewModel()));

        var all = toolkit.CreateTools(WorkflowToolCategory.All);
        var def = toolkit.CreateTools();
        Assert.HasCount(def.Count, all, "default CreateTools() must equal CreateTools(All)");

        Assert.IsTrue(all.Any(t => string.Equals(t.Name, "CreateNode", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(all.Any(t => string.Equals(t.Name, "ExecuteWork", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(all.Any(t => string.Equals(t.Name, "SearchForward", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(all.Any(t => string.Equals(t.Name, "AutoLayout", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(all.Any(t => string.Equals(t.Name, "BatchExecute", StringComparison.OrdinalIgnoreCase)));
        // Interaction tools require a registered handler — absent even under All without one.
        Assert.IsFalse(all.Any(t => string.Equals(t.Name, "RequestSelection", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SetEnumSlotCollection_DoesNotAddPhantomUndoEntries()
    {
        var session = WorkflowDemoSession.Create();
        var tree = session.Tree;
        var node = tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        int idx = tree.Nodes.IndexOf(node);
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree));

        var initialType = node.EnumType;
        Assert.IsNotNull(initialType);
        // The demo's initial selector is VoltageRange (internal to Lib); NetworkRequestMethod
        // is public and guaranteed different, so the switch below is a real change.
        Assert.AreNotEqual(typeof(NetworkRequestMethod), initialType);

        var result = InvokeTool(toolkit, "SetEnumSlotCollection",
            ("nodeIndex", idx),
            ("propertyName", "OutputSlots"),
            ("selectorTypeOrJson", typeof(NetworkRequestMethod).FullName!));
        var json = JObject.Parse(result);
        Assert.IsTrue(json["ok"]?.Value<bool>() ?? false, result);
        Assert.AreEqual(typeof(NetworkRequestMethod), node.EnumType);

        // A single Undo must reverse the selector switch — the anchor refresh must NOT have
        // pushed phantom ±0.5px move entries onto the undo stack.
        tree.GetHelper().Undo();
        Assert.AreEqual(initialType, node.EnumType,
            "one Undo should reverse the selector switch, not a phantom nudge move");
    }

    [TestMethod]
    public async Task CloneNodes_UndoRemovesAllClonesInOneStep()
    {
        var session = WorkflowDemoSession.Create();
        var tree = session.Tree;
        int originalCount = tree.Nodes.Count;
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree));

        var result = InvokeTool(toolkit, "CloneNodes",
            ("nodeIndicesJson", "[0]"),
            ("offsetX", 300.0),
            ("offsetY", 0.0));
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>());

        // CreateNodeCommand is fire-and-forget async, so poll until the clone lands in the tree.
        await WaitUntilAsync(() => tree.Nodes.Count == originalCount + 1, "cloned node should appear");

        // A single Undo must reverse the whole clone (node + its slots), not just one sub-step.
        tree.GetHelper().Undo();
        await WaitUntilAsync(() => tree.Nodes.Count == originalCount,
            "one Undo should remove every cloned node — CloneNodes must be a single undo gesture");
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
    public void ConnectSlots_ReturnsOnlyAfterConnectionApplied()
    {
        var tree = new TreeDefaultViewModel();
        var nodeA = new NodeDefaultViewModel();
        var nodeB = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(nodeA);
        tree.GetHelper().CreateNode(nodeB);
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree));

        var slotType = typeof(SlotDefaultViewModel).FullName!;
        InvokeTool(toolkit, "AddSlotToCollection", ("nodeIndex", 0), ("propertyName", "Slots"), ("fullSlotTypeName", slotType), ("channel", "OneBoth"));
        InvokeTool(toolkit, "AddSlotToCollection", ("nodeIndex", 1), ("propertyName", "Slots"), ("fullSlotTypeName", slotType), ("channel", "OneBoth"));

        var result = InvokeTool(toolkit, "ConnectSlots",
            ("senderNodeIndex", 0), ("senderSlotIndex", 0),
            ("receiverNodeIndex", 1), ("receiverSlotIndex", 0));
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), result);

        // The tool awaits Send/Receive completion, so the connection is visible immediately —
        // no stale-state window for a subsequent GetNodeDetail/ListConnections call.
        var senderSlot = nodeA.Slots[0];
        var receiverSlot = nodeB.Slots[0];
        Assert.IsTrue(tree.LinksMap.TryGetValue(senderSlot, out var targets) && targets.ContainsKey(receiverSlot),
            "connection must be visible as soon as ConnectSlots returns");
    }

    [TestMethod]
    public void ListCreatableTypes_IncludesScopeRegisteredAssemblies()
    {
        // Register Lib's assembly via auto-discovery but keep the tree empty — Lib's node types
        // must still surface because the scope knows their assembly.
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree)
            .WithAutoDiscovery(typeof(EnumSelectorNodeViewModel).Assembly);
        var toolkit = new WorkflowAgentToolkit(scope);

        var result = InvokeTool(toolkit, "ListCreatableTypes");
        var json = JObject.Parse(result);
        var nodeTypes = json["nodeTypes"] as JArray;
        Assert.IsNotNull(nodeTypes);
        Assert.IsTrue(nodeTypes!.Any(n => string.Equals(n["name"]?.ToString(), "EnumSelectorNodeViewModel", StringComparison.Ordinal)),
            "ListCreatableTypes must include types from scope-registered assemblies even with an empty tree");
    }

    [TestMethod]
    public void MaxWriteToolCalls_RejectsMutations_ButAllowsQueries()
    {
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree).WithMaxWriteToolCalls(0);
        var toolkit = new WorkflowAgentToolkit(scope);

        // Mutation is rejected once the write budget is exhausted.
        var mutateText = InvokeWrappedTool(toolkit, "MarkDirty");
        Assert.IsTrue(mutateText.Contains("limit", StringComparison.OrdinalIgnoreCase),
            "a mutation tool must be rejected when the write budget is exhausted");

        // Reads do not consume the write budget.
        var queryText = InvokeWrappedTool(toolkit, "GetTypeSchema", ("fullTypeName", "System.String"));
        var queryJson = JObject.Parse(queryText);
        Assert.AreEqual("System.String", queryJson["fullName"]?.ToString(),
            "a query tool must still work — reads do not consume the write budget");
    }

    [TestMethod]
    public void MaxReadToolCalls_RejectsQueries_ButAllowsMutations()
    {
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree).WithMaxReadToolCalls(0);
        var toolkit = new WorkflowAgentToolkit(scope);

        // Query is rejected once the read budget is exhausted.
        var queryText = InvokeWrappedTool(toolkit, "GetTypeSchema", ("fullTypeName", "System.String"));
        Assert.IsTrue(queryText.Contains("limit", StringComparison.OrdinalIgnoreCase),
            "a query tool must be rejected when the read budget is exhausted");

        // Writes do not consume the read budget.
        var mutateText = InvokeWrappedTool(toolkit, "MarkDirty");
        var mutateJson = JObject.Parse(mutateText);
        Assert.AreEqual("ok", mutateJson["status"]?.Value<string>(),
            "a mutation tool must still work — writes do not consume the read budget");
    }

    private static string InvokeWrappedTool(WorkflowAgentToolkit toolkit, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = toolkit.CreateTools().Single(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        var callArgs = new AIFunctionArguments();
        foreach (var (n, v) in args)
            callArgs[n] = v;
        var result = ((AIFunction)tool).InvokeAsync(callArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    [TestMethod]
    public void CustomTool_RegisteredWithWithTools_IsTracked_AndAutoMarksDirty()
    {
        var tree = new TreeDefaultViewModel();
        var helper = new TestTreeHelper();
        tree.SetHelper(helper);
        var scope = new WorkflowAgentScope(tree)
            .WithAutoMarkDirty(true)
            .WithTools("a mutation custom tool", CustomTool("MyCustomMutate", () => "ok"));
        var toolkit = new WorkflowAgentToolkit(scope);

        var tool = scope.ProvideTools().Single(t => string.Equals(t.Name, "MyCustomMutate", StringComparison.OrdinalIgnoreCase));
        ((AIFunction)tool).InvokeAsync(new AIFunctionArguments(), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        Assert.IsGreaterThan(0, helper.MarkDirtyCount,
            "a WithTools custom tool must be wrapped and auto-mark dirty when AutoMarkDirty is on");
    }

    [TestMethod]
    public void QueryCustomTool_RegisteredWithWithQueryTools_DoesNotMarkDirty()
    {
        var tree = new TreeDefaultViewModel();
        var helper = new TestTreeHelper();
        tree.SetHelper(helper);
        var scope = new WorkflowAgentScope(tree)
            .WithAutoMarkDirty(true)
            .WithQueryTools("a read-only custom tool", CustomTool("MyCustomQuery", () => "[]"));
        var toolkit = new WorkflowAgentToolkit(scope);

        var tool = scope.ProvideTools().Single(t => string.Equals(t.Name, "MyCustomQuery", StringComparison.OrdinalIgnoreCase));
        ((AIFunction)tool).InvokeAsync(new AIFunctionArguments(), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual(0, helper.MarkDirtyCount,
            "a WithQueryTools custom tool must never auto-mark dirty");
    }

    [TestMethod]
    public void CustomTool_Invocation_FiresToolCalledCallback()
    {
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree)
            .WithTools("ctx", CustomTool("MyCustomTool", () => "result"));
        string? calledName = null;
        int callCount = 0;
        scope.WithToolCallCallback(args =>
        {
            calledName = args.ToolName;
            callCount = args.CallCount;
            return Task.CompletedTask;
        });

        var tool = scope.ProvideTools().Single(t => string.Equals(t.Name, "MyCustomTool", StringComparison.OrdinalIgnoreCase));
        ((AIFunction)tool).InvokeAsync(new AIFunctionArguments(), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        Assert.AreEqual("MyCustomTool", calledName, "wrapped custom tool must raise the ToolCalled callback");
        Assert.AreEqual(1, callCount);
    }

    private static AIFunction CustomTool(string name, Func<string> body)
        => AIFunctionFactory.Create(() => body(), name);

    private static string InvokeTool(WorkflowAgentToolkit toolkit, string toolName, params (string Name, object? Value)[] args)
    {
        var method = typeof(WorkflowAgentToolkit)
            .GetMethod(toolName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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
            var match = args.FirstOrDefault(a => string.Equals(a.Name, parameters[i].Name, StringComparison.OrdinalIgnoreCase));
            invocationArgs[i] = match == default ? parameters[i].DefaultValue : match.Value;
        }

        var raw = method.Invoke(toolkit, invocationArgs);
        if (raw is Task<string> asyncResult)
            raw = asyncResult.GetAwaiter().GetResult();
        Assert.IsInstanceOfType<string>(raw);

        var trackMethod = typeof(WorkflowAgentToolkit)
            .GetMethod("TrackAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(trackMethod, "TrackAsync method was not found.");

        var trackedTask = (Task)trackMethod.Invoke(toolkit, [toolName, (string)raw!])!;
        trackedTask.GetAwaiter().GetResult();
        return (string)raw!;
    }

    private sealed class TestTreeHelper : TreeHelper
    {
        public int MarkDirtyCount { get; private set; }

        public override void MarkDirty()
        {
            MarkDirtyCount++;
            base.MarkDirty();
        }
    }
}
