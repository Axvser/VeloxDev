using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.Core.Extension.Test.Examples;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>
/// Carrying a run's place onto a graph that came back from serialization.
/// <para>
/// A checkpoint is filed by node identity, and a restored graph has fresh identities — so the engine refuses to
/// resume onto it, which is right (those really are different node objects). <see cref="ExecutionCheckpoint.Rekey"/>
/// is the host's opt-in that says "I know they are, and here is the mapping", and this is what that unlocks: the
/// shape a crash recovery actually has — save the place, reload the graph, carry on.
/// </para>
/// </summary>
[TestClass]
public class ExecutionCheckpointMigrationTests
{
    private readonly List<string> _scratch = [];

    private readonly List<WorkflowDemoSession> _sessions = [];

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
        var directory = Path.Combine(Path.GetTempPath(), $"veloxdev-rekey-{Guid.NewGuid():N}");
        _scratch.Add(directory);
        return directory;
    }

    /// <summary>The demo graph with a stubbed interpreter, so a run costs neither a process nor a Python install.</summary>
    private WorkflowDemoSession Stubbed(TimeSpan? perCall = null)
    {
        var session = WorkflowDemoSession.Create(Scratch());
        _sessions.Add(session);
        foreach (var node in session.Tree.Nodes.OfType<PythonScriptNodeViewModel>())
            node.SetHelper(new StubPythonHelper { PerCallDelay = perCall ?? TimeSpan.Zero });
        return session;
    }

    private static IEnumerable<PythonScriptNodeViewModel> PythonNodes(CompiledGraph graph)
        => graph.Entries.SelectMany(Enumerate)
            .OfType<ChainSegment>()
            .SelectMany(chain => chain.Nodes)
            .OfType<PythonScriptNodeViewModel>();

    private static IEnumerable<CompileSegment> Enumerate(CompileSegment entry)
    {
        yield return entry;
        switch (entry)
        {
            case BranchSegment branch:
                foreach (var option in branch.Options)
                    if (option.Graph is { } sub)
                        foreach (var nested in sub.Entries.SelectMany(Enumerate)) yield return nested;
                break;
            case ParallelSegment parallel:
                foreach (var sub in parallel.Branches)
                    foreach (var nested in sub.Entries.SelectMany(Enumerate)) yield return nested;
                break;
        }
    }

    private static async Task RunCommandAsync(IVeloxCommand command)
    {
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        command.Exited += _ => finished.TrySetResult(true);
        await command.ExecuteAsync(null);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Save a place mid-run, reload the graph from its own snapshot, re-key the place onto it, and carry on: the
    /// nodes the place records as done must not run again on the reloaded graph either.
    /// </summary>
    [TestMethod]
    public async Task ARekeyedPlace_CarriesARunOntoAReloadedGraph()
    {
        var session = Stubbed(TimeSpan.FromMilliseconds(20));
        await RunCommandAsync(session.Controller.CompileCommand);

        // Stop in the second pass: the first is the quick set the audit is built to refuse, so the second is where
        // a place worth carrying on from actually exists.
        var anomaly = session.Tree.Nodes.OfType<PythonScriptNodeViewModel>().First(n => n.Title == "Anomaly Scan");
        ((StubPythonHelper)anomaly.GetHelper()).CancelAtInvocation = 2;
        await RunCommandAsync(session.Controller.RunCommand);

        var place = await session.Checkpoints.LoadAsync(CancellationToken.None);
        Assert.IsNotNull(place, "the stopped run wrote its place");
        Assert.IsNotEmpty(place.Outputs, "precondition: something had finished before it stopped");

        // The graph as a reloaded one: same structure, fresh node identities — the engine refuses to resume onto it.
        var restored = session.Controller.Compiler.Graphs[0].SerializeCompiledGraph().DeserializeCompiledGraph();
        Assert.IsNotNull(restored, "the snapshot round trip must produce a graph");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await new RuntimeEngine().RunAsync(restored, new RuntimeContext(), CancellationToken.None, place));

        // Re-keyed, it fits — and every node's state is back on its own node, positionally.
        var rekeyed = ExecutionCheckpoint.Rekey(place, restored);
        Assert.IsNotEmpty(rekeyed.Shape);
        Assert.HasCount(place.Outputs.Count, rekeyed.Outputs, "every output found its node");

        var stubs = new Dictionary<string, StubPythonHelper>();
        foreach (var node in PythonNodes(restored)) stubs[node.Title] = new StubPythonHelper();
        foreach (var node in PythonNodes(restored)) node.SetHelper(stubs[node.Title]);

        var resumed = new RuntimeContext { CheckpointStore = session.Checkpoints };
        await new RuntimeEngine().RunAsync(restored, resumed, CancellationToken.None, rekeyed);

        Assert.AreEqual("Completed", resumed.Status, "the reloaded graph ran to its end");
        Assert.HasCount(0, stubs["Generate Dataset"].Trail,
            "the generator had finished, so the reloaded graph must not drive it again");
        Assert.HasCount(1, stubs["Merge Report"].Trail,
            "the merge had not finished, so the reloaded graph is where it runs");
    }

    [TestMethod]
    public async Task ARekeyOntoAGraphOfADifferentSize_IsRefused()
    {
        // A chain of one node: same kind of checkpoint, nothing like the same graph.
        var smaller = new CompiledGraph();
        var chain = new ChainSegment();
        chain.Nodes.Add(new PythonScriptNodeViewModel());
        smaller.Entries.Add(chain);

        var place = new ExecutionCheckpoint { Shape = ["a", "b", "c"], Types = ["X", "Y", "Z"] };

        var refused = Assert.ThrowsExactly<InvalidOperationException>(() => ExecutionCheckpoint.Rekey(place, smaller));
        StringAssert.Contains(refused.Message, "no positional mapping", refused.Message);
    }

    /// <summary>
    /// The structural check an identity-less reload needs: the same count is not enough, the nodes have to be the
    /// same kinds in the same drive order — otherwise the outputs would land on the wrong nodes.
    /// </summary>
    [TestMethod]
    public void ARekeyOntoASameSizedGraphOfOtherTypes_IsRefused()
    {
        var target = new CompiledGraph();
        var chain = new ChainSegment();
        chain.Nodes.Add(new ControllerViewModel());
        target.Entries.Add(chain);

        var place = new ExecutionCheckpoint { Shape = ["a"], Types = ["PythonScriptNodeViewModel"] };

        var refused = Assert.ThrowsExactly<InvalidOperationException>(() => ExecutionCheckpoint.Rekey(place, target));
        StringAssert.Contains(refused.Message, "not the same structure", refused.Message);
    }
}
