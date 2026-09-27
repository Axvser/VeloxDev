using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// A run's place: what it has already driven, what those nodes produced, and enough of the run's own state to
/// carry on from there. Written by <see cref="RuntimeEngine.RunAsync"/> after each node succeeds when a
/// <see cref="IExecutionCheckpointStore"/> is configured, and handed back to it to resume.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a snapshot, not a log.</b> <see cref="Outputs"/> holds every node that counts as done — the current
/// pass's, plus the prefix a redirect preserved — and each save replaces the last.
/// </para>
/// <para>
/// <b>What makes it usable is <see cref="Shape"/>.</b> A checkpoint belongs to one graph, and the shape is how the
/// engine knows which: resuming onto a graph whose shape differs is refused rather than guessed at, because the
/// alternative is driving the wrong nodes with someone else's payloads.
/// </para>
/// </remarks>
/// <seealso cref="IExecutionCheckpointStore"/>
public sealed class ExecutionCheckpoint
{
    /// <summary>The pass the run was on when this was taken — <see cref="IRuntimeContext.Attempt"/>.</summary>
    public int Attempt { get; set; }

    /// <summary>The redirect target that pass was working with, if any — <see cref="IRuntimeContext.ActiveRedirectTarget"/>.</summary>
    public int? ActiveRedirectTarget { get; set; }

    /// <summary>
    /// The chained payload at the moment of the snapshot. An <see cref="IGroupData"/> is stored as a plain
    /// dictionary keyed by node key, since a node reference cannot be written down (and would drag the tree in).
    /// </summary>
    public object? Data { get; set; }

    /// <summary>What each completed node produced, keyed by <see cref="KeyOf"/>.</summary>
    public Dictionary<string, object?> Outputs { get; set; } = [];

    /// <summary>The graph's nodes in drive order — the fingerprint a resume checks itself against.</summary>
    public List<string> Shape { get; set; } = [];

    // 图里的节点，按引擎驱动的顺序，各自带上检查点给它归档的键。
    // 顺序即遍历顺序：链按次序、分支先路由器再依次下钻每个选项的子图、扇出按分支序。
    internal static IReadOnlyList<(IWorkflowNodeViewModel Node, string Key)> NodesOf(CompiledGraph graph)
    {
        var nodes = new List<(IWorkflowNodeViewModel, string)>();
        if (graph is null) return nodes;

        Walk(graph, nodes);
        return nodes;
    }

    // 图的形状 —— NodesOf 去掉节点，只留键。
    internal static List<string> ShapeOf(CompiledGraph graph)
    {
        var shape = new List<string>();
        foreach (var (_, key) in NodesOf(graph)) shape.Add(key);
        return shape;
    }

    // 节点在检查点里的身份：`RuntimeId`（同一棵树重新编译也不变，因为编译不重建节点），
    // 或者对没有身份的节点用「类型名#序号」。
    // 后果要记住：序列化往返过的图 RuntimeId 全是新的 ⇒ 它的指纹对不上、恢复会被拒 —— 这正是想要的，
    // 那些确实是不同的节点对象，把旧产物喂过去就是猜。
    private static string KeyOf(IWorkflowNodeViewModel node, int index)
        => node is IWorkflowIdentifiable identifiable && !string.IsNullOrEmpty(identifiable.RuntimeId)
            ? identifiable.RuntimeId
            : $"{node.GetType().Name}#{index}";

    // 深度优先：分支先记路由器、再按选项顺序下钻子图；扇出按分支序。
    private static void Walk(CompiledGraph graph, List<(IWorkflowNodeViewModel, string)> nodes)
    {
        foreach (var entry in graph.Entries)
        {
            switch (entry)
            {
                case ChainSegment chain:
                    foreach (var node in chain.Nodes)
                        if (node is not null) nodes.Add((node, KeyOf(node, nodes.Count)));
                    break;

                case BranchSegment branch:
                    if (branch.Router is { } router) nodes.Add((router, KeyOf(router, nodes.Count)));
                    foreach (var option in branch.Options)
                        if (option?.Graph is { } subgraph) Walk(subgraph, nodes);
                    break;

                case ParallelSegment parallel:
                    foreach (var sub in parallel.Branches)
                        if (sub is not null) Walk(sub, nodes);
                    break;
            }
        }
    }
}

/// <summary>
/// The checkpoint store that keeps the place in memory — enough for a run that pauses and resumes inside one
/// process, and the default a host gets when it asks for checkpointing without wanting to write files.
/// </summary>
/// <remarks>
/// Concurrent saves are serialised with a lock: the engine can have two fan-out branches saving at once. The
/// checkpoint handed in is kept as it is, so a host that plans to mutate one afterwards should hand over a copy.
/// </remarks>
public sealed class InMemoryCheckpointStore : IExecutionCheckpointStore
{
    private readonly object _lock = new();
    private ExecutionCheckpoint? _checkpoint;

    /// <summary>Whether anything has been saved.</summary>
    public bool HasCheckpoint
    {
        get { lock (_lock) return _checkpoint is not null; }
    }

    /// <inheritdoc />
    public Task SaveAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        lock (_lock) _checkpoint = checkpoint;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ExecutionCheckpoint?> LoadAsync(CancellationToken cancellationToken)
    {
        lock (_lock) return Task.FromResult(_checkpoint);
    }

    /// <summary>Drops what was saved — for a host whose run is finished and whose place is no longer worth keeping.</summary>
    public void Clear()
    {
        lock (_lock) _checkpoint = null;
    }
}
