using CliWrap;
using CliWrap.Buffered;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Newtonsoft.Json;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Coverage for the new FULL-demo content: the compiled compute chain (Controller → Timer fan-out → two Python
/// workers → join → LogicGate → report branches), the dynamic Python port provider, and the Python/LogicGate
/// helpers. The real Python execution smoke test is gated on a Python interpreter being present.
/// </summary>
[TestClass]
public class NewDemoComputeChainTests
{
    [TestMethod]
    public async Task NewDemo_CompilesToFanOutJoinAndBranches()
    {
        var session = WorkflowDemoSession.Create();
        var compiler = new CompilerViewModel();
        var graphs = await compiler.CompileAsync(session.Controller);
        Assert.AreEqual(1, graphs.Count, "single start node → single compiled graph");
        var graph = graphs[0];

        // Structure: [Controller chain] → [Timer branch → Generate Dataset fan-out (Parallel) → join Merge → Enum branch].
        Assert.IsInstanceOfType<ExecuteEntry>(graph.Entries[0], "first entry is the Controller chain");
        var timerBranch = graph.Entries[1];
        Assert.IsInstanceOfType<BranchEntry>(timerBranch, "Timer is the single-key router");
        var tickOption = ((BranchEntry)timerBranch).Options.First(o => Equals(o.Key, "tick"));
        var timerSub = tickOption.Graph!;
        Assert.IsInstanceOfType<ExecuteEntry>(timerSub.Entries[0], "Generate Dataset is the linear segment after the Timer");
        Assert.IsInstanceOfType<ParallelEntry>(timerSub.Entries[1],
            "the plain-node fan-out (Generate Dataset → 3 analyzers) compiles to a ParallelEntry");
        Assert.AreEqual(3, ((ParallelEntry)timerSub.Entries[1]).Branches.Count,
            "three analyzers run as the fan-out branches");
        Assert.IsInstanceOfType<ExecuteEntry>(timerSub.Entries[2], "the join node (Merge Report) follows the fan-out");
        Assert.IsInstanceOfType<BranchEntry>(timerSub.Entries[3], "the Enum Selector is the decision router");
        Assert.IsTrue(((BranchEntry)timerSub.Entries[3]).IsDynamic, "the enum routes at runtime by the grade");

        // Compile identity: Controller 0, Timer 1, Generate Dataset 2, Merge Report 6, Enum 7.
        var timer = session.Tree.Nodes.OfType<TimerNodeViewModel>().Single();
        var generate = session.Tree.Nodes.OfType<PythonScriptNodeViewModel>().Single(n => n.Title == "Generate Dataset");
        var merge = session.Tree.Nodes.OfType<PythonScriptNodeViewModel>().Single(n => n.Title == "Merge Report");
        var enumSelector = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        Assert.AreEqual(0, session.Controller.CompileContext?.Order, "controller is order 0");
        Assert.AreEqual(1, timer.CompileContext?.Order, "timer is order 1");
        Assert.AreEqual(2, generate.CompileContext?.Order, "fan-out source is order 2");
        Assert.AreEqual(6, merge.CompileContext?.Order, "join node continues after the three analyzers");
        Assert.AreEqual(7, enumSelector.CompileContext?.Order, "enum follows the join");

        // Join registration: Merge Report registers all three analyzers as its inputs.
        Assert.AreEqual(3, merge.CompileContext?.InputNodes?.Count,
            "the join node registered all three analyzers for GroupData aggregation");
        Assert.IsFalse(session.Tree.Nodes.OfType<PythonScriptNodeViewModel>().Any(p => p.IsCompileStopped),
            "dynamic routing keeps every Python node alive");
    }

    [TestMethod]
    public void PythonPortProvider_ProducesSlotsFromPorts()
    {
        var provider = new PythonPortProvider { Ports = [new PythonPort("in"), new PythonPort("alpha")] };
        var slots = provider.GetSlots().ToArray();
        Assert.AreEqual(2, slots.Length, "one SlotDefinition per port");
        Assert.AreEqual("in", slots[0].Value, "port name is the routing key");
        Assert.AreEqual("alpha", slots[1].Label, "port name doubles as the label");
    }

    [TestMethod]
    public void PythonPortProvider_JsonRoundTrips()
    {
        var json = JsonConvert.SerializeObject(
            new PythonPortProvider { Ports = [new PythonPort("alpha"), new PythonPort("beta")] });
        var back = JsonConvert.DeserializeObject<PythonPortProvider>(json);
        Assert.AreEqual(2, back!.Ports.Count, "JSON round-trip preserves the ports");
        Assert.AreEqual("alpha", back.Ports[0].Name);
        Assert.AreEqual("beta", back.Ports[1].Name);
    }

    [TestMethod]
    public void BuildInputPayload_MapsGroupDataByPortName()
    {
        var py3 = new PythonScriptNodeViewModel();
        py3.InputSlots.SetSelector(new PythonPortProvider { Ports = [new PythonPort("alpha"), new PythonPort("beta")] });
        var py1 = new PythonScriptNodeViewModel();
        var py2 = new PythonScriptNodeViewModel();
        py1.OutputSlots.SetSelector(new PythonPortProvider { Ports = [new PythonPort("alpha")] });
        py2.OutputSlots.SetSelector(new PythonPortProvider { Ports = [new PythonPort("beta")] });

        // Wire py1.alpha → py3.alpha, py2.beta → py3.beta (the input port's Sources carry the sender slots).
        py3.InputSlots.Items[0].Slot.Sources.Add(py1.OutputSlots.Items[0].Slot);
        py3.InputSlots.Items[1].Slot.Sources.Add(py2.OutputSlots.Items[0].Slot);

        var group = new GroupData(new Dictionary<IWorkflowNodeViewModel, object?>
        {
            [py1] = new Dictionary<string, object?> { ["alpha"] = 7 },
            [py2] = new Dictionary<string, object?> { ["beta"] = 11 },
        });

        var helper = new PythonHelper();
        helper.Install(py3);
        var payload = helper.BuildInputPayload(new RuntimeContext { Data = group });

        Assert.IsInstanceOfType(payload, typeof(Dictionary<string, object?>),
            "join payload is a plain object so the script sees meaningful field names");
        var dict = (Dictionary<string, object?>)payload!;
        Assert.IsTrue(dict.ContainsKey("alpha"), "port 'alpha' maps to its source's output");
        Assert.IsTrue(dict.ContainsKey("beta"), "port 'beta' maps to its source's output");
        Assert.IsFalse(dict.ContainsKey("unwired"), "unregistered ports are absent");
    }

    [TestMethod]
    public void BuildInputPayload_PassesSingleValueThrough_WhenNotJoined()
    {
        var node = new PythonScriptNodeViewModel();
        var helper = new PythonHelper();
        helper.Install(node);
        var ctx = new RuntimeContext { Data = new Dictionary<string, object?> { ["time"] = "seed" } };
        Assert.AreSame(ctx.Data, helper.BuildInputPayload(ctx),
            "a non-join node passes the upstream output through unchanged");
    }

    [TestMethod]
    public void ParseResult_HandlesJsonObjectPlainJsonAndText()
    {
        var obj = PythonHelper.ParseResult("{\"score\": 5, \"pass\": 1}");
        Assert.IsInstanceOfType<Dictionary<string, object?>>(obj, "JSON object → dictionary");

        var scalar = PythonHelper.ParseResult("[1, 2]");
        Assert.IsNotNull(scalar, "other JSON parses too (JToken)");

        var text = PythonHelper.ParseResult("not json");
        Assert.AreEqual("not json", text, "non-JSON output falls back to the raw string");

        Assert.IsNull(PythonHelper.ParseResult(null), "null/empty → null");
    }

    [TestMethod]
    public void LogicGateHelper_Evaluate_ReducesNumberBoolStringAndDict()
    {
        Assert.IsFalse(LogicGateHelper.Evaluate(0, GateOp.Identity), "0 → false");
        Assert.IsTrue(LogicGateHelper.Evaluate(1, GateOp.Identity), "1 → true");
        Assert.IsTrue(LogicGateHelper.Evaluate(true, GateOp.Identity), "bool passes through");
        Assert.IsFalse(LogicGateHelper.Evaluate(1, GateOp.Not), "Not inverts a truthy value");
        Assert.IsTrue(LogicGateHelper.Evaluate(0, GateOp.Not), "Not inverts a falsy value");
        Assert.IsTrue(LogicGateHelper.Evaluate("pass", GateOp.Identity), "pass string → true");
        Assert.IsFalse(LogicGateHelper.Evaluate("", GateOp.Identity), "empty string → false");

        var pass = new Dictionary<string, object?> { ["pass"] = 1L };
        var fail = new Dictionary<string, object?> { ["pass"] = 0L };
        Assert.IsTrue(LogicGateHelper.Evaluate(pass, GateOp.Identity),
            "dict {pass:1} (long from JSON) → true");
        Assert.IsFalse(LogicGateHelper.Evaluate(fail, GateOp.Identity),
            "dict {pass:0} → false");
    }

    [TestMethod]
    public async Task PythonNode_RunsRealScript_WhenPythonAvailable()
    {
        if (!await PythonAvailableAsync())
        {
            Assert.Inconclusive("python is not on PATH; skipping the real-execution smoke test.");
            return;
        }

        var node = new PythonScriptNodeViewModel
        {
            Script = "import json, sys\n"
                   + "d = json.load(open(sys.argv[1], encoding='utf-8'))\n"
                   + "json.dump({'echo': d.get('x'), 'sum': d['a'] + d['b']}, open(sys.argv[2], 'w', encoding='utf-8'))",
        };
        var helper = new PythonHelper();
        helper.Install(node);

        var ctx = new RuntimeContext
        {
            Data = new Dictionary<string, object?> { ["x"] = 42, ["a"] = 2, ["b"] = 3 },
        };
        var result = await helper.ReceiveAsync(ctx, CancellationToken.None);

        Assert.IsInstanceOfType(result, typeof(Dictionary<string, object?>), "script result is parsed back");
        var dict = (Dictionary<string, object?>)result!;
        Assert.AreEqual(42L, Convert.ToInt64(dict!["echo"]), "the value round-trips through a real python process");
        Assert.AreEqual(5L, Convert.ToInt64(dict["sum"]), "the script actually computed something");
    }

    [TestMethod]
    public async Task NewDemo_RunsEndToEnd_WhenPythonAvailable()
    {
        if (!await PythonAvailableAsync())
        {
            Assert.Inconclusive("python is not on PATH; skipping the end-to-end run.");
            return;
        }

        try
        {
            var session = WorkflowDemoSession.Create();
            var compiler = new CompilerViewModel();
            await compiler.CompileAsync(session.Controller);

            var context = new RuntimeContext();
            await new CompilerEngine().RunAsync(compiler.Graphs[0], context, CancellationToken.None);

            Assert.AreEqual("Completed", context.Status,
                "the voltage chain must run end to end: " + string.Join("\n", context.Logs.TakeLast(8)));
            Assert.IsFalse(context.EndedWithError, "no python error terminated the run");
            Assert.IsTrue(context.Logs.Any(l => l.Contains("EnumSelectorNodeViewModel")), "the enum router was driven");
        }
        finally
        {
            foreach (var f in Directory.GetFiles(AppContext.BaseDirectory, "report_*.csv"))
                try { File.Delete(f); } catch { /* best-effort */ }
        }
    }

    private static async Task<bool> PythonAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            await Cli.Wrap("python").WithArguments("--version").ExecuteBufferedAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
