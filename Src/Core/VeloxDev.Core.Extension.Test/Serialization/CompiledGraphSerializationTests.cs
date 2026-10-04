using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Extension.Test.Serialization;

/// <summary>The grade the probe router routes on — an enum, i.e. the key kind a round trip degrades.</summary>
public enum ProbeGrade { Low, High }

/// <summary>
/// A router that executes nothing (the default <c>NodeHelper.ReceiveAsync</c> returns <c>null</c>), so a restored graph
/// can be driven in a test without running a script — while still routing on an enum key that is resolved <b>at run
/// time</b>. That is the case a degraded key breaks: the run-time key is a real enum and a restored <c>long</c> matches
/// no option.
/// </summary>
[WorkflowBuilder.Node<NodeHelper<ProbeRouterNode>>(workSemaphore: 1)]
public partial class ProbeRouterNode : ICompileTimeRouter
{
    public ProbeRouterNode() => InitializeWorkflow();

    /// <summary>Where each grade routes.</summary>
    public IWorkflowNodeViewModel? LowBranch { get; set; }
    public IWorkflowNodeViewModel? HighBranch { get; set; }

    /// <summary>The grade resolved at run time.</summary>
    public ProbeGrade Resolved { get; set; } = ProbeGrade.High;

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable()
        => Task.FromResult<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>>(
            new Dictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>
            {
                [ProbeGrade.Low] = LowBranch is null ? [] : [LowBranch],
                [ProbeGrade.High] = HighBranch is null ? [] : [HighBranch],
            });

    /// <inheritdoc />
    /// <remarks>Null at compile time — which is what makes the compiled branch <b>dynamic</b> — and the grade at run time.</remarks>
    public Task<object?> ResolveRouteKey(object? payload)
        => Task.FromResult<object?>(payload is null ? null : Resolved);
}

/// <summary>
/// The compiled graph as a document. Until now nothing in the repo had ever round-tripped one — these are the first
/// tests that put a <see cref="CompiledGraph"/> through <see cref="ComponentModelEx"/>.
/// <para>
/// The graph under test is the demo session's own: controller → timer → generator → fan-out → join → enum-keyed
/// router → three branches. It is built by production code (<see cref="WorkflowDemoSession.Create"/>), so the shape
/// — fan-out, a static-or-dynamic branch and a join — needs no hand-wiring and no probe stubs.
/// </para>
/// </summary>
[TestClass]
public class CompiledGraphSerializationTests
{
    private static (WorkflowDemoSession Session, TreeViewModel Tree, CompiledGraph Graph) CompiledDemo()
    {
        var session = WorkflowDemoSession.Create();
        var graphs = new CompilerViewModel()
            .CompileAsync(session.Controller, CompileRole.Root)
            .GetAwaiter().GetResult();

        Assert.IsTrue(graphs.Count > 0, "the demo graph must compile");
        return (session, session.Tree, graphs[0]);
    }

    /// <summary>The structure the compiler produced is exactly what comes back.</summary>
    [TestMethod]
    public void ASnapshot_RoundTripsTheSegmentStructure()
    {
        var (_, _, graph) = CompiledDemo();
        var before = Describe(graph);

        var json = graph.SerializeCompiledGraph();
        var restored = json.DeserializeCompiledGraph();

        Assert.IsNotNull(restored);
        Assert.AreEqual(before, Describe(restored), "the segment structure must survive the round trip");
        Assert.IsTrue(before.Contains("Parallel"), "precondition: the demo graph has a fan-out");
        Assert.IsTrue(before.Contains("Branch"), "precondition: the demo graph has a routed branch");
    }

    /// <summary>
    /// The reason the keys carry a type name: an enum in an <c>object</c> member comes back as its number, and a
    /// dynamic branch would then match no option at all. The demo routes twice — the timer on strings, the grade
    /// selector on enum members — so this asserts the whole type sequence, which fails on an <c>Int64</c> anywhere.
    /// </summary>
    [TestMethod]
    public void ASnapshot_RestoresBranchKeysWithTheTypeTheyWentInWith()
    {
        var (_, _, graph) = CompiledDemo();
        var liveKeys = BranchKeys(graph).ToList();
        Assert.IsTrue(liveKeys.Any(k => k is string),
            "precondition: the timer routes on string keys");
        Assert.IsTrue(liveKeys.Any(k => k?.GetType().IsEnum == true),
            $"precondition: the grade selector routes on enum members; live keys were [{string.Join(", ", liveKeys.Select(Show))}]");

        var restored = graph.SerializeCompiledGraph().DeserializeCompiledGraph();
        var restoredKeys = BranchKeys(restored!).ToList();

        Assert.HasCount(liveKeys.Count, restoredKeys, "every branch option must survive");
        CollectionAssert.AreEqual(
            liveKeys.Select(Show).ToList(),
            restoredKeys.Select(Show).ToList(),
            "each key must come back as the same value with the same type — an enum restored as its number is the "
            + "failure this side channel exists to prevent");
    }

    /// <summary>Renders a key for a comparison message: its type then its value.</summary>
    private static string Show(object? key) => key is null ? "(null)" : $"{key.GetType().Name}:{key}";

    /// <summary>
    /// A number that matches no member must not throw: it becomes an undefined enum value, exactly like a live run
    /// handed a key no option names — the flow ends where it ends, with nothing fabricated.
    /// </summary>
    [TestMethod]
    public void ARestoredKey_ThatMatchesNoMember_IsAnUndefinedValueRatherThanAnError()
    {
        var option = new BranchOption
        {
            Key = 42L,
            KeyTypeName = typeof(ProbeGrade).AssemblyQualifiedName,
            Label = "42",
        };

        var restored = option.Serialize().Deserialize<BranchOption>();

        Assert.AreEqual(typeof(ProbeGrade), restored.Key!.GetType(), "the type is still restored");
        Assert.AreEqual(ProbeGrade.Low.GetType(), restored.Key.GetType());
        Assert.IsFalse(Enum.IsDefined(typeof(ProbeGrade), restored.Key),
            "42 is not a member, so the value is undefined — and that is the intended outcome, not an exception");
    }

    /// <summary>A type name that no longer resolves leaves the value alone rather than guessing at it.</summary>
    [TestMethod]
    public void ARestoredKey_WhoseTypeNameDoesNotResolve_IsLeftAsItCame()
    {
        var option = new BranchOption
        {
            Key = 7L,
            KeyTypeName = "Some.Assembly.That.Is.Not.Loaded+ProbeGrade, Nowhere",
            Label = "7",
        };

        var restored = option.Serialize().Deserialize<BranchOption>();

        Assert.AreEqual(7L, restored.Key, "an unresolvable type name must not change the value");
    }

    /// <summary>
    /// The end-to-end claim the key side channel exists for: a snapshot of a <b>dynamic, enum-keyed</b> branch still
    /// routes after a round trip, and drives the branch the run-time key names — not its sibling.
    /// <para>
    /// Without the type name the restored option key would be a number, the run-time key is a real enum, no option
    /// would match, and the run would end before driving any branch node: the negative assertion below is what
    /// catches that, and the two branches deliberately have <i>different</i> node types so the log can tell them
    /// apart.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task ARestoredGraph_DynamicallyRoutesOnItsEnumKey()
    {
        var router = new ProbeRouterNode { Resolved = ProbeGrade.Low };
        router.LowBranch = new NodeDefaultViewModel();
        router.HighBranch = new TestEnumNode();

        var graphs = await new CompilerViewModel().CompileAsync(router, CompileRole.Root);
        var graph = graphs.Single();
        var branch = graph.Entries.OfType<BranchSegment>().Single();
        Assert.IsTrue(branch.IsDynamic,
            "precondition: the branch must be dynamic, or its key is never compared at run time");
        Assert.IsNull(branch.CompileKey, "precondition: a dynamic branch locks no compile-time key");

        var restored = graph.SerializeCompiledGraph().DeserializeCompiledGraph();
        var session = new RuntimeContext();
        await new RuntimeEngine().RunAsync(restored!, session, CancellationToken.None);

        Assert.AreEqual("Completed", session.Status);
        Assert.IsFalse(session.EndedWithError);
        Assert.IsTrue(session.Logs.Any(l => l.Contains(nameof(NodeDefaultViewModel))),
            $"the Low branch must have been driven; logs were: {string.Join(" | ", session.Logs)}");
        Assert.IsFalse(session.Logs.Any(l => l.Contains(nameof(TestEnumNode))),
            "the High branch must not have been driven");
    }

    /// <summary>
    /// What the snapshot mode is for: the document stops at the graph. The sentinel makes the assertion about the
    /// <i>tree's content</i> rather than about a type name, and the size comparison is the property that fails today
    /// (a node's <c>Parent</c> is writable, so any node drags the whole document in).
    /// </summary>
    [TestMethod]
    public void ASnapshot_DoesNotDragInTheTree()
    {
        var (_, tree, graph) = CompiledDemo();
        var sentinel = $"sentinel-{Guid.NewGuid():N}";
        tree.ConversationMarkdown = sentinel;

        var snapshot = graph.SerializeCompiledGraph();
        var withTree = graph.SerializeCompiledGraph(includeTree: true);

        Assert.IsFalse(snapshot.Contains(sentinel),
            "a snapshot must not reach the tree through a node's back-pointer");
        Assert.IsTrue(withTree.Contains(sentinel),
            "the other mode is supposed to carry the tree — otherwise this test proves nothing about the sentinel");
        Assert.IsTrue(snapshot.Length < withTree.Length,
            $"the snapshot must be the smaller document (snapshot {snapshot.Length} vs with-tree {withTree.Length})");
    }

    /// <summary>
    /// Two facts about a restored node that a host has to know rather than discover: it is wired to <b>nothing</b>
    /// (so nothing re-collapses its geometry for the zoom and nothing marks a tree dirty when it moves), and it gets
    /// a <b>fresh</b> identity. The nodes are also the same instances as the canvas's before serializing — the graph
    /// re-houses them rather than copying them.
    /// </summary>
    [TestMethod]
    public void ASnapshot_RestoresNodesWithNoParentAndAFreshIdentity()
    {
        var (_, tree, graph) = CompiledDemo();
        var liveNode = FirstNode(graph);
        Assert.IsTrue(tree.Nodes.Contains(liveNode),
            "the compiled graph must hold the canvas's own node instances, not copies");

        var restored = graph.SerializeCompiledGraph().DeserializeCompiledGraph();
        var restoredNode = FirstNode(restored!);

        Assert.IsNull(restoredNode.Parent,
            "a snapshot drops the back-pointer — the restored node is not attached to any tree");
        Assert.AreNotEqual(
            ((IWorkflowIdentifiable)liveNode).RuntimeId,
            ((IWorkflowIdentifiable)restoredNode).RuntimeId,
            "RuntimeId is a get-only member, so a restored node gets a fresh one");
    }

    /// <summary>A one-line-per-segment description, so a structure mismatch is readable in the failure message.</summary>
    private static string Describe(CompiledGraph graph)
    {
        static void Walk(CompiledGraph g, System.Text.StringBuilder sb, int depth)
        {
            foreach (var entry in g.Entries)
            {
                switch (entry)
                {
                    case ChainSegment chain:
                        sb.AppendLine($"{new string(' ', depth)}Chain[{string.Join(",", chain.Nodes.Select(n => n.GetType().Name))}]");
                        break;
                    case BranchSegment branch:
                        sb.AppendLine($"{new string(' ', depth)}Branch(dynamic={branch.IsDynamic},key={branch.CompileKey})");
                        foreach (var option in branch.Options)
                        {
                            sb.AppendLine($"{new string(' ', depth + 1)}Option({option.Label},terminal={option.IsTerminal})");
                            if (option.Graph is not null) Walk(option.Graph, sb, depth + 2);
                        }
                        break;
                    case ParallelSegment parallel:
                        sb.AppendLine($"{new string(' ', depth)}Parallel({parallel.Branches.Count})");
                        foreach (var b in parallel.Branches) Walk(b, sb, depth + 1);
                        break;
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        Walk(graph, sb, 0);
        return sb.ToString();
    }

    /// <summary>Every branch option's key, in document order — across nested graphs, so both routers are covered.</summary>
    private static System.Collections.Generic.IEnumerable<object?> BranchKeys(CompiledGraph? graph)
    {
        if (graph is null) yield break;
        foreach (var entry in graph.Entries)
        {
            switch (entry)
            {
                case BranchSegment branch:
                    foreach (var option in branch.Options)
                    {
                        yield return option.Key;
                        // A branch's sub-graph holds the rest of the chain — including, in the demo, the second
                        // router. Walking only the root's entries would miss every nested branch.
                        foreach (var nested in BranchKeys(option.Graph)) yield return nested;
                    }
                    break;
                case ParallelSegment parallel:
                    foreach (var b in parallel.Branches)
                        foreach (var key in BranchKeys(b)) yield return key;
                    break;
            }
        }
    }

    private static IWorkflowNodeViewModel FirstNode(CompiledGraph graph)
    {
        foreach (var entry in graph.Entries)
        {
            if (entry is ChainSegment chain && chain.Nodes.Count > 0) return chain.Nodes[0];
            if (entry is ParallelSegment parallel && parallel.Branches.Count > 0) return FirstNode(parallel.Branches[0]);
        }

        throw new InvalidOperationException("the compiled graph has no chain to take a node from");
    }
}
