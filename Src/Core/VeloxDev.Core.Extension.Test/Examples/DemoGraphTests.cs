using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Examples;

/// <summary>
/// The demo graph is the reference picture of what the compiled-execution engine does: one press of Run shows a
/// chain, a compile-time-pruned static branch, an interleaved fan-out and its join, warnings that stop nothing, an
/// error that asks for a re-run instead of ending the run, a retry, and a second pass — with the log going to a
/// file, an observer summarizing the run, failures collected as records and the run's place checkpointed.
/// <para>
/// This drives that graph headlessly, through the controller's own Run command (so the demo's own session
/// configuration is part of what is under test). The interpreter is stubbed — <see cref="DemoScriptTests"/> runs the
/// real scripts, and skips itself when the machine has no Python — so what is asserted here is the graph, the
/// conventions the helper implements around a script, and the engine's reactions to them.
/// </para>
/// </summary>
[TestClass]
public class DemoGraphTests
{
    /// <summary>
    /// Stands in for the interpreter, answering by node title: it returns what the real script would return,
    /// including the diagnostic fields the host reacts to (<c>warn</c> / <c>error</c> / <c>redirect</c>) and the
    /// engine-state keys it was handed (<c>_attempt</c> / <c>_drive</c>). Everything around it — the payload the
    /// helper builds, the drive counter, the error/redirect handling — is the real code.
    /// </summary>
    private sealed class StubPythonHelper : PythonHelper
    {
        private readonly Dictionary<string, int> _drives = [];

        /// <summary>One line per invocation — what the node was handed, which is what a failure here is about.</summary>
        public List<string> Trail { get; } = [];

        /// <summary>
        /// When positive, the invocation with this number stops the run the way a host's Stop does. The engine reads
        /// an <see cref="OperationCanceledException"/> as cancellation rather than failure, so the run ends at that
        /// node boundary and everything already driven stays in the checkpoint.
        /// </summary>
        public int CancelAtInvocation { get; set; } = -1;

        private int _invocations;

        /// <summary>
        /// Called on this stub's own invocation number — a run is held or stopped from inside it, which is where a
        /// host's Pause and Stop land anyway: the engine only ever looks at the gate at a node boundary.
        /// </summary>
        public Action<int>? OnInvocation { get; set; }

        public int Drives(string title) => _drives.TryGetValue(title, out var n) ? n : 0;

        protected override Task<string> InvokePythonAsync(string script, object? payload, string pythonExe, CancellationToken ct)
        {
            var input = payload as IDictionary<string, object?> ?? new Dictionary<string, object?>();
            var title = Component?.Title ?? string.Empty;
            var attempt = Read(input, "_attempt");
            var drive = Read(input, "_drive");
            _drives[title] = drive;
            Trail.Add($"{title}|drive={drive}|attempt={attempt}|payload={payload?.GetType().Name ?? "null"}");

            var invocation = ++_invocations;
            OnInvocation?.Invoke(invocation);
            if (CancelAtInvocation > 0 && invocation >= CancelAtInvocation)
                throw new OperationCanceledException(ct);

            // A failed delivery, not a reported one: the point of this node is the retry policy, and only a throw
            // reaches it — a script that reports an error is asking the run to stop, which is a different thing.
            if (title == "Publish" && drive == 1)
                throw new InvalidOperationException("transient: the report sink refused the connection (simulated)");

            return Task.FromResult(Answer(title, input, attempt, drive));
        }

        private static int Read(IDictionary<string, object?> input, string key)
            => input.TryGetValue(key, out var value) ? Convert.ToInt32(value) : 0;

        /// <summary>Writes what the real script would write — including what it forwards, since the audit reads it.</summary>
        private static string Answer(string title, IDictionary<string, object?> input, int attempt, int drive) => title switch
        {
            // First pass: a flat pair of samples, which the audit below is meant to refuse.
            "Generate Dataset" => attempt == 1
                ? """{"samples":[220.0,220.0],"count":2,"note":"quick pass"}"""
                : """{"samples":[175.0,320.0,178.0],"count":3,"note":"full pass"}""",

            "Load Sample File" => """{"samples":[220.0],"count":1,"unit":"V"}""",

            "Numeric Stats" => """{"count":3,"mean":224.3,"median":178.0,"stdev":68.1,"min":175.0,"max":320.0,"p95":320.0,"range":145.0}""",

            "Frequency Dist" => attempt == 1
                ? """{"histogram":[{"bin":"220-240","count":2}],"total":2,"warn":"empty histogram bin(s): <200, >240"}"""
                : """{"histogram":[{"bin":"<200","count":1},{"bin":"220-240","count":1},{"bin":">240","count":1}],"total":3}""",

            "Anomaly Scan" => attempt == 1
                ? """{"anomalies":[],"count":0,"threshold_z":2.0,"warn":"no anomaly in 2 samples: the set looks flat"}"""
                : """{"anomalies":[{"index":1,"value":320.0,"z":2.4}],"count":1,"threshold_z":2.0}""",

            "Merge Report" => attempt == 1
                ? """{"summary":{"count":2},"sample_count":2,"anomaly_count":0,"mean_voltage":220.0,"grade":"Zero","High":0,"Low":0,"Zero":1}"""
                : """{"summary":{"count":40},"sample_count":40,"anomaly_count":1,"mean_voltage":224.3,"grade":"Low","High":0,"Low":1,"Zero":0}""",

            // Carries the report on, exactly as the real script does: the audit behind this node reads it.
            "Publish" => Forward(input, new() { ["published"] = true, ["attempt"] = attempt }),

            // The audit: refuses the thin set by naming the node to fall back to, and passes the report through
            // untouched once the data is good (the selector below routes on the flags it carries).
            "Audit" => Read(input, "sample_count") < 20
                ? """{"verified":false,"sample_count":2,"error":"too few samples got published, run the pipeline again","redirect":"Ticker"}"""
                : Forward(input, new() { ["verified"] = true }),

            // The payload is null when the audit sent the run back, and the real script warns instead of crashing.
            "Report High" or "Report Low" or "Report Zero" => input.Count == 0
                ? """{"warn":"nothing to archive this pass: the audit sent the run back"}"""
                : """{"saved_to":"report_low.csv","grade":"Low","records":4}""",

            _ => "{}",
        };

        private static string Forward(IDictionary<string, object?> input, Dictionary<string, object?> extra)
        {
            var output = new Dictionary<string, object?>(input);
            foreach (var entry in extra) output[entry.Key] = entry.Value;
            return System.Text.Json.JsonSerializer.Serialize(output);
        }
    }

    /// <summary>One scratch directory per test: the checkpoint and the log are per-run files, not shared state.</summary>
    private readonly List<string> _scratch = [];

    private readonly List<WorkflowDemoSession> _sessions = [];

    // 先释放会话再删目录：日志是会话开的文件，句柄还开着时目录删不掉。
    [TestCleanup]
    public void Cleanup()
    {
        foreach (var session in _sessions) session.Dispose();
        _sessions.Clear();
        foreach (var directory in _scratch)
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        _scratch.Clear();
    }

    private string Scratch()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-demo-graph-{Guid.NewGuid():N}");
        _scratch.Add(directory);
        return directory;
    }

    private WorkflowDemoSession Stubbed()
    {
        var session = WorkflowDemoSession.Create(Scratch());
        _sessions.Add(session);
        foreach (var node in session.Tree.Nodes.OfType<PythonScriptNodeViewModel>())
            node.SetHelper(new StubPythonHelper());
        return session;
    }

    private static IEnumerable<StubPythonHelper> Stubs(WorkflowDemoSession session)
        => session.Tree.Nodes.OfType<PythonScriptNodeViewModel>().Select(n => (StubPythonHelper)n.GetHelper());

    private static int Drives(WorkflowDemoSession session) => Stubs(session).Sum(h => h.Trail.Count);

    /// <summary>
    /// Runs through the controller's own command. <c>ExecuteAsync</c> queues the work and returns; the command's
    /// <c>Exited</c> event is the completion signal (the same one the Agent's <c>ExecuteNode</c> waits on).
    /// </summary>
    private static async Task RunCommandAsync(IVeloxCommand command)
    {
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        command.Exited += _ => finished.TrySetResult(true);
        await command.ExecuteAsync(null);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static async Task<WorkflowDemoSession> RunAsync(WorkflowDemoSession session)
    {
        await CompileAsync(session);
        await RunCommandAsync(session.Controller.RunCommand);
        return session;
    }

    /// <summary>Compiles and waits for the command to have really finished — <c>ExecuteAsync</c> only queues it.</summary>
    private static async Task CompileAsync(WorkflowDemoSession session)
    {
        await RunCommandAsync(session.Controller.CompileCommand);
        Assert.IsNotEmpty(session.Controller.Compiler.Graphs, "the demo graph must compile before it can be driven");
    }

    private static PythonScriptNodeViewModel PythonNode(WorkflowDemoSession session, string title)
        => session.Tree.Nodes.OfType<PythonScriptNodeViewModel>().First(n => n.Title == title);

    /// <summary>Every segment of a compiled graph, nested ones included — a branch's options hold graphs of their own.</summary>
    private static IEnumerable<CompileSegment> Segments(CompiledGraph graph)
    {
        foreach (var entry in graph.Entries)
        {
            yield return entry;
            switch (entry)
            {
                case BranchSegment branch:
                    foreach (var option in branch.Options)
                        if (option.Graph is { } sub)
                            foreach (var nested in Segments(sub)) yield return nested;
                    break;
                case ParallelSegment parallel:
                    foreach (var sub in parallel.Branches)
                        foreach (var nested in Segments(sub)) yield return nested;
                    break;
            }
        }
    }

    /// <summary>
    /// The compiled shape is the compile half of the story: static pruning, a dynamic router with all its options,
    /// a fan-out, and the nodes a pruned branch leaves behind at <c>Order = -1</c>.
    /// </summary>
    [TestMethod]
    public async Task TheCompiledGraph_CarriesEveryShapeTheEngineDrives()
    {
        var session = WorkflowDemoSession.Create(Scratch());
        await CompileAsync(session);
        var graph = session.Controller.Compiler.Graphs.First();

        var branches = Segments(graph).OfType<BranchSegment>().ToList();
        // The ticker is a router too (a node with a single target still compiles to a branch), so the two selectors
        // are asked about by name rather than by counting branch segments.
        var staticBranch = branches.Single(b => !b.IsDynamic && b.Router is EnumSelectorNodeViewModel);
        var dynamicBranch = branches.Single(b => b.IsDynamic && b.Router is EnumSelectorNodeViewModel);
        Assert.HasCount(1, staticBranch.Options, "a static branch keeps only the option that was selected at compile time");
        Assert.AreEqual(3, dynamicBranch.Options.Count, "a dynamic branch keeps all of them");

        Assert.IsTrue(Segments(graph).OfType<ParallelSegment>().Any(), "the three analyzers are one fan-out group");
        Assert.IsNotEmpty(Segments(graph).OfType<ChainSegment>());

        var pruned = PythonNode(session, "Load Sample File");
        Assert.AreEqual(-1, pruned.CompileContext?.Order, "the pruned option's node stops at the absolute-stop order");
        Assert.IsTrue(pruned.IsCompileStopped, "and says so on the canvas");
    }

    [TestMethod]
    public async Task OneRun_ShowsEveryCapabilityTheGraphWasBuiltFor()
    {
        var session = await RunAsync(Stubbed());
        var runtime = Assert.IsInstanceOfType<RuntimeContext>(session.Controller.RuntimeContext);

        // It finished, and it finished on the second pass: the validator sent it back.
        Assert.AreEqual("Completed", runtime.Status, string.Join(" | ", runtime.Logs));
        Assert.AreEqual(RunOutcome.Completed, runtime.Outcome);
        Assert.AreEqual(2, runtime.Attempt, "the redirect re-ran the graph exactly once");

        var generate = PythonNode(session, "Generate Dataset");
        var audit = PythonNode(session, "Audit");
        var drives = new Dictionary<string, int>();
        foreach (var node in session.Tree.Nodes.OfType<PythonScriptNodeViewModel>())
        {
            var helper = (StubPythonHelper)node.GetHelper();
            drives[node.Title] = helper.Drives(node.Title);
        }

        Assert.AreEqual(2, drives["Generate Dataset"],
            "driven once per pass — the redirect sent the run back to it; log: " + string.Join(" | ", runtime.Logs));
        Assert.AreEqual(2, drives["Audit"], "and the audit itself ran on both passes");
        Assert.AreEqual(2, drives["Numeric Stats"], "a fan-out branch runs on each pass");
        var publishTrail = ((StubPythonHelper)PythonNode(session, "Publish").GetHelper()).Trail;
        Assert.AreEqual(3, drives["Publish"],
            $"twice on the first pass (fail, retry) and once on the second; trail: {string.Join(" ; ", publishTrail)}");
        Assert.IsTrue(publishTrail.Count >= 2 && publishTrail[0].Contains("drive=1") && publishTrail[1].Contains("drive=2"),
            $"the retry re-drives the same node with a bumped drive counter — that is how a script tells the two apart; got: {string.Join(" ; ", publishTrail)}");
        Assert.AreEqual(0, drives["Load Sample File"], "the pruned branch is never driven");

        // Warnings that stop nothing, an error that asks for a re-run, and a retry that got through.
        Assert.IsTrue(session.Diagnostics.Any(e => e.Level == ExecutionReportLevel.Warning), "the flat set warns twice");
        Assert.IsTrue(session.Diagnostics.Any(e => e.Level == ExecutionReportLevel.Error), "and the audit reports an error");
        Assert.IsTrue(runtime.Logs.Any(l => l.Contains("[Retry 1]", StringComparison.Ordinal)),
            $"the publish step must show its retry; got: {string.Join(" | ", runtime.Logs)}");
        Assert.IsTrue(runtime.Logs.Any(l => l.Contains("[Observer]", StringComparison.Ordinal)),
            "the observer closes the run with one line about it");

        // The two files a host can go and look at.
        Assert.IsTrue(File.Exists(session.LogPath), $"the run's log is written to a file: {session.LogPath}");
        Assert.IsTrue(File.Exists(session.CheckpointPath), $"and its place is checkpointed: {session.CheckpointPath}");
        var place = await session.Checkpoints.LoadAsync(CancellationToken.None);
        Assert.IsNotNull(place);
        Assert.IsTrue(place.Shape.Count > 10, "the checkpoint names the graph's nodes");
        Assert.IsNotEmpty(place.Outputs, "and what they produced");

        // The node-level view the canvas binds.
        Assert.AreEqual("Completed", generate.LastStatus);
        Assert.AreEqual("Completed", audit.LastStatus);
        Assert.IsNull(audit.RedirectTo, "the fallback target is cleared at the start of the drive that follows");
    }

    /// <summary>
    /// The pause control the demo's sidebar binds: the session hands its gate to every run, and a held gate stops
    /// the run before the first node rather than somewhere arbitrary inside one.
    /// </summary>
    [TestMethod]
    public async Task TheDemoSessionsGate_HoldsTheRun_UntilItIsLetGo()
    {
        var session = Stubbed();
        await CompileAsync(session);

        // Pause from inside the second node: the gate is only ever read at a node boundary, so that is exactly
        // where a host's Pause button takes effect too.
        StubOf(session, "Numeric Stats").OnInvocation = n => { if (n == 1) session.Gate.Pause(); };

        var run = RunCommandAsync(session.Controller.RunCommand);
        await Task.Delay(300);
        Assert.AreEqual(2, Drives(session), "a held run stops at the boundary after the node that paused it");
        Assert.IsFalse(run.IsCompleted, "and it is parked, not finished");
        Assert.IsTrue(session.Gate.IsPaused);

        session.Gate.Resume();
        await run;

        Assert.IsTrue(Drives(session) > 5, "letting it go carries the run on");
        Assert.AreEqual("Completed", ((RuntimeContext)session.Controller.RuntimeContext!).Status);
        Assert.IsFalse(session.Gate.IsPaused);
    }

    /// <summary>
    /// Stop in the middle, then continue: the two controls the sidebar offers for a run that did not finish. What
    /// the stopped run had already driven is not driven again, and the nodes behind it still see those outputs.
    /// <para>
    /// The stop lands in the second pass on purpose. This graph's first pass is the quick set the audit is built to
    /// refuse, so everything it produced is thrown away by the redirect — the place worth carrying on from is the
    /// one the second pass left behind.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task StoppingARun_AndContinuingFromItsCheckpoint_SkipsWhatWasAlreadyDone()
    {
        var session = Stubbed();
        await CompileAsync(session);

        // The anomaly scan stops the run on its second drive — the one that belongs to the pass the redirect
        // started. An OperationCanceledException is read as cancellation, so the run ends at that node boundary and
        // what came before stays in the checkpoint. (The counter is per stub: each node counts its own drives.)
        StubOf(session, "Anomaly Scan").CancelAtInvocation = 2;
        await RunCommandAsync(session.Controller.RunCommand);

        var stopped = Assert.IsInstanceOfType<RuntimeContext>(session.Controller.RuntimeContext);
        Assert.AreEqual(RunOutcome.Cancelled, stopped.Outcome, "the run stopped, it did not fail");
        Assert.AreEqual(2, stopped.Attempt, "it had already been sent back once by the audit");
        Assert.IsTrue(session.HasCheckpoint, "and it left its place behind — that is what the Resume control reads");

        var place = await session.Checkpoints.LoadAsync(CancellationToken.None);
        Assert.IsNotNull(place);
        var generatorKey = ((IWorkflowIdentifiable)PythonNode(session, "Generate Dataset")).RuntimeId;
        Assert.IsTrue(place.Outputs.ContainsKey(generatorKey),
            "the place files the generator's success under its own identity, not under a position");

        var generateBefore = StubOf(session, "Generate Dataset").Trail.Count;
        var mergeBefore = StubOf(session, "Merge Report").Trail.Count;
        Assert.AreEqual(2, generateBefore, "precondition: both passes drove the generator");
        Assert.AreEqual(1, mergeBefore, "precondition: the merge ran on the first pass only — the second was stopped");

        StubOf(session, "Anomaly Scan").CancelAtInvocation = -1;
        await RunCommandAsync(session.Controller.ResumeCommand);

        var resumed = Assert.IsInstanceOfType<RuntimeContext>(session.Controller.RuntimeContext);
        Assert.AreEqual("Completed", resumed.Status);
        Assert.AreEqual(2, resumed.Attempt, "the resume carries on in the pass the checkpoint was taken in");
        Assert.AreEqual(generateBefore, StubOf(session, "Generate Dataset").Trail.Count,
            "the generator is not driven again: the checkpoint records it as done, and the restored report is the " +
            "full one, so the audit has no reason to send the run back this time");
        Assert.AreEqual(mergeBefore + 1, StubOf(session, "Merge Report").Trail.Count,
            "the merge had not run when the run stopped, so the resume drives it");
    }

    private static StubPythonHelper StubOf(WorkflowDemoSession session, string title)
        => (StubPythonHelper)PythonNode(session, title).GetHelper();
}
