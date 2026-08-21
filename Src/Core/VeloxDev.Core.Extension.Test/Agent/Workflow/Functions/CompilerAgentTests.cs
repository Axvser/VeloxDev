using Demo.ViewModels;
using Demo.Workflow;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Threading;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Coverage for the Compiler support added to the Agent: framework context exposure (B),
/// the embedded CompilerUsage skill (A), and the CompileWorkflow / GetCompileStatus tools (C).
/// </summary>
[TestClass]
public class CompilerAgentTests
{
    // ── B: compiler concepts surface in framework context ──

    [TestMethod]
    public void FrameworkContext_IncludesCompilerTypes()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        var fwData = scope.ProvideFrameworkDataContext();
        Assert.IsTrue(fwData.Contains("Type: VeloxDev.Core.WorkflowSystem.CompilerEx.ICompileContext"));
        Assert.IsTrue(fwData.Contains("Type: VeloxDev.Core.WorkflowSystem.CompilerEx.IRuntimeContext"));

        var fwContext = scope.ProvideFrameworkContext();
        Assert.IsTrue(fwContext.Contains("Type: VeloxDev.Core.WorkflowSystem.CompilerEx.RouterCompileMode"),
            "RouterCompileMode must render as a framework enum so the Agent understands Static/Dynamic");
    }

    // ── A: CompilerUsage skill is embedded (en + zh) and loaded into skills ──

    [TestMethod]
    public void CompilerSkill_IsEmbeddedInBothLanguages()
    {
        var en = AgentEmbeddedResources.ReadAllSkills("Workflow", AgentLanguages.English);
        var zh = AgentEmbeddedResources.ReadAllSkills("Workflow", AgentLanguages.Chinese);

        Assert.IsTrue(en.Contains("Compile-Time Routing"), "English CompilerUsage skill must be embedded");
        Assert.IsTrue(en.Contains("CompileWorkflow"), "English skill must reference the compile tools");
        Assert.IsTrue(en.Contains("Two execution entries") && en.Contains("RunCompiledWorkflow"),
            "English skill must document the dual-entry model (node-level vs chain-level)");
        Assert.IsTrue(zh.Contains("编译期路由"), "Chinese CompilerUsage skill must be embedded");
        Assert.IsTrue(zh.Contains("两个执行入口"), "Chinese skill must document the dual-entry model");
    }

    // ── C: compile tools are available as query tools ──

    [TestMethod]
    public void CompilerTools_AreQueryCategory()
    {
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(new TreeDefaultViewModel()));

        var query = toolkit.CreateTools(WorkflowToolCategory.Query);
        Assert.IsTrue(query.Any(t => t.Name == "CompileWorkflow"));
        Assert.IsTrue(query.Any(t => t.Name == "GetCompileStatus"));
    }

    // ── C: CompileWorkflow compiles the demo graph ──

    [TestMethod]
    public void CompileWorkflow_ReturnsPlanAndOrders()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(session.Tree));
        int controllerIdx = session.Tree.Nodes.IndexOf(session.Controller);

        var result = InvokeTool(toolkit, "CompileWorkflow", ("startNodeIndex", controllerIdx));
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), result);
        Assert.IsGreaterThan(0, json["graphCount"]?.Value<int>() ?? 0, "demo graph must compile");

        var entries = json["entries"] as JArray;
        Assert.IsNotNull(entries);
        Assert.IsGreaterThan(0, entries!.Count, "compiled plan must contain entries");
        Assert.IsGreaterThan(0, entries!.Count(e => e["type"]?.Value<string>() == "Branch"),
            "the demo graph contains compile-time routers (Bool/Enum selector) → Branch entries");

        var orders = json["nodeOrders"] as JArray;
        Assert.IsNotNull(orders);
        Assert.IsGreaterThan(0, orders!.Count, "compile must assign identity to compile-aware nodes");
        foreach (var n in orders!)
        {
            Assert.IsNotNull(n["order"], "each compiled node must carry Order");
            Assert.IsNotNull(n["chainIndex"]);
            Assert.IsNotNull(n["offset"]);
            Assert.IsNotNull(n["isStopped"]);
        }
    }

    [TestMethod]
    public void GetCompileStatus_ReturnsIdentityWithoutRecompiling()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(session.Tree));
        int controllerIdx = session.Tree.Nodes.IndexOf(session.Controller);

        InvokeTool(toolkit, "CompileWorkflow", ("startNodeIndex", controllerIdx));
        var result = InvokeTool(toolkit, "GetCompileStatus");
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), result);

        var nodes = json["nodes"] as JArray;
        Assert.IsNotNull(nodes);
        Assert.IsGreaterThan(0, nodes!.Count, "GetCompileStatus must report compiled nodes");
    }

    // ── C: Static compile prunes unselected branches (Order = -1) ──

    [TestMethod]
    public void StaticCompile_PrunesUnselectedBranches()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var tree = session.Tree;
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree));
        var enumSelector = tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        int enumIdx = tree.Nodes.IndexOf(enumSelector);
        int controllerIdx = tree.Nodes.IndexOf(session.Controller);

        // Baseline: Dynamic (default) compile leaves every branch alive — nothing stopped.
        var dynamicResult = InvokeTool(toolkit, "CompileWorkflow", ("startNodeIndex", controllerIdx));
        var dynamicJson = JObject.Parse(dynamicResult);
        Assert.AreEqual(0, dynamicJson["nodeOrders"]!.Count(o => o["isStopped"]?.Value<bool>() == true),
            "Dynamic compile must keep all branches alive");

        // Switch the Method Router to Static while GET is selected → non-GET handlers pruned.
        var patch = InvokeTool(toolkit, "PatchNodeProperties",
            ("nodeIndex", enumIdx), ("jsonPatch", "{\"CompileMode\":\"Static\"}"));
        var patchJson = JObject.Parse(patch);
        Assert.AreEqual("ok", patchJson["status"]?.Value<string>(), patch);
        Assert.AreEqual(RouterCompileMode.Static, enumSelector.CompileMode, "PatchNodeProperties must set CompileMode");

        var staticResult = InvokeTool(toolkit, "CompileWorkflow", ("startNodeIndex", controllerIdx));
        var staticJson = JObject.Parse(staticResult);
        var stopped = staticJson["nodeOrders"]!
            .Where(o => o["isStopped"]?.Value<bool>() == true)
            .Select(o => o["t"]?.Value<string>())
            .ToList();
        var live = staticJson["nodeOrders"]!
            .Where(o => o["isStopped"]?.Value<bool>() == false)
            .Select(o => o["t"]?.Value<string>())
            .ToList();

        Assert.IsGreaterThan(0, stopped.Count, "static compile must prune non-selected branches");
        Assert.IsTrue(stopped.All(t => t == "NodeViewModel"),
            "the pruned nodes are the non-selected handler NodeViewModels");
        Assert.IsTrue(live.Contains("NodeViewModel"),
            "the selected (GET) handler must stay live");
    }

    // ── C2: RunCompiledWorkflow — the chain-level execution entry ──

    [TestMethod]
    public void RunCompiledWorkflow_IsExecutionCategory_AndGated()
    {
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(new TreeDefaultViewModel()));

        var exec = toolkit.CreateTools(WorkflowToolCategory.Execution);
        Assert.IsTrue(exec.Any(t => t.Name == "RunCompiledWorkflow"),
            "RunCompiledWorkflow must be an execution tool (chain-level run)");
        Assert.IsFalse(toolkit.CreateTools(WorkflowToolCategory.Query).Any(t => t.Name == "RunCompiledWorkflow"),
            "RunCompiledWorkflow runs node business code → not a query tool");

        // Gated by AllowNodeExecution (same as ExecuteNode): default scope refuses.
        var result = InvokeTool(toolkit, "RunCompiledWorkflow", ("startNodeIndex", 0));
        Assert.IsTrue(result.Contains("disabled by host policy"), result);
    }

    [TestMethod]
    public void RunCompiledWorkflow_DrivesTheChain()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        // Zero the simulated delays so the whole chain completes fast in the test.
        foreach (var n in session.Nodes)
            n.DelayMilliseconds = 0;

        var tree = session.Tree;
        var scope = new WorkflowAgentScope(tree).WithAllowNodeExecution(true);
        var toolkit = new WorkflowAgentToolkit(scope);
        int controllerIdx = tree.Nodes.IndexOf(session.Controller);

        var result = InvokeTool(toolkit, "RunCompiledWorkflow",
            ("startNodeIndex", controllerIdx), ("seed", "demo-request-chain"));
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), result);
        Assert.AreEqual("Completed", json["runStatus"]?.Value<string>(),
            "the compiled chain should run to completion through the engine: " + result);
        Assert.IsFalse(json["endedWithError"]?.Value<bool>() ?? false, result);
        Assert.IsGreaterThan(0, json["attempts"]?.Value<int>() ?? 0, "the run must count attempts");

        var logs = json["logs"] as JArray;
        Assert.IsNotNull(logs);
        Assert.IsGreaterThan(0, logs!.Count, "the chain run must produce an execution log");
    }

    // ── Demo numbering fix: non-compiler starts must NOT clobber the compile machine's badges ──

    [TestMethod]
    public async Task NonCompilerStart_DoesNotClobberCompileBadge()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        foreach (var n in session.Nodes) n.DelayMilliseconds = 0;
        var tree = session.Tree;
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree).WithAllowNodeExecution(true));
        int controllerIdx = tree.Nodes.IndexOf(session.Controller);

        // Compile the machine → every node gets a compile identity (badge #Order+1).
        InvokeTool(toolkit, "CompileWorkflow", ("startNodeIndex", controllerIdx));
        var loadSeed = tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Load Seed");
        loadSeed.AutoBroadcast = false;   // keep the isolated start truly single-node
        int compileBadge = loadSeed.LastExecutionOrder;
        Assert.IsGreaterThan(0, compileBadge, "compile must assign a machine badge");

        // Direct (non-compiler) start → the machine badge must NOT change.
        int idx = tree.Nodes.IndexOf(loadSeed);
        InvokeTool(toolkit, "ExecuteNode", ("nodeIndex", idx));
        await WaitUntilAsync(() => loadSeed.LastStatus == "Completed", "isolated start should complete");
        Assert.AreEqual(compileBadge, loadSeed.LastExecutionOrder,
            "a non-compiler direct start must NOT clobber the compile-machine badge");
    }

    [TestMethod]
    public async Task GetExecutionLog_ReturnsTreeLog()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var tree = session.Tree;
        var loadSeed = tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Load Seed");
        loadSeed.DelayMilliseconds = 0;
        loadSeed.AutoBroadcast = false;
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree).WithAllowNodeExecution(true));
        int idx = tree.Nodes.IndexOf(loadSeed);

        InvokeTool(toolkit, "ExecuteNode", ("nodeIndex", idx));
        await WaitUntilAsync(() => tree.ExecutionLog.Count > 0, "the non-compiler run must append to the tree log");

        var result = InvokeTool(toolkit, "GetExecutionLog");
        var json = JObject.Parse(result);
        Assert.AreEqual("ok", json["status"]?.Value<string>(), result);
        Assert.IsGreaterThan(0, json["entryCount"]?.Value<int>() ?? 0, "the tree log must be readable by the Agent");
        var entries = json["entries"] as JArray;
        Assert.IsNotNull(entries);
        Assert.IsTrue(entries!.Count(e => e.ToString().Contains("Load Seed")) > 0,
            "the log must contain the executed node's entry");
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

    // ── helper (mirrors WorkflowAgentToolkitTests.InvokeTool) ──

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
            var match = args.FirstOrDefault(a => string.Equals(a.Name, parameters[i].Name, StringComparison.OrdinalIgnoreCase));
            invocationArgs[i] = match == default ? parameters[i].DefaultValue : match.Value;
        }

        var raw = method.Invoke(toolkit, invocationArgs);
        if (raw is Task<string> asyncResult)
            raw = asyncResult.GetAwaiter().GetResult();
        Assert.IsInstanceOfType<string>(raw);
        return (string)raw!;
    }
}
