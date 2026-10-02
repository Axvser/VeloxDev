using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Serialization;
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
[VeloxSerializable]
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

    /// <summary>
    /// The node <b>types</b> behind <see cref="Shape"/>, in the same order. Written so a re-key has something to
    /// check structure by when the old graph object is gone; empty on a checkpoint written before this member
    /// existed, which makes a re-key fall back to counting nodes only.
    /// </summary>
    public List<string> Types { get; set; } = [];

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

    /// <summary>
    /// Re-keys a checkpoint so it fits <paramref name="target"/> — the same graph structure with different node
    /// identities, which is what a round trip through serialization produces.
    /// </summary>
    /// <param name="checkpoint">The place, as it was written.</param>
    /// <param name="target">The graph to fit it to.</param>
    /// <returns>A new checkpoint, filed under <paramref name="target"/>'s identities. The input is untouched.</returns>
    /// <exception cref="InvalidOperationException">
    /// The two graphs are not the same structure — refused rather than guessed at, because the alternative is
    /// driving this graph's nodes with that graph's outputs.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Why it exists.</b> A checkpoint is filed by <see cref="IWorkflowIdentifiable.RuntimeId"/>, and a graph
    /// that came back from serialization has fresh ones — so resuming onto it is refused by
    /// <see cref="RuntimeEngine.RunAsync"/>. That refusal is right: those really are different node objects. This
    /// is the host's opt-in that says <i>I know they are, and here is the mapping</i> — positional, because the
    /// traversal order is the one thing the same structure always shares.
    /// </para>
    /// <para>
    /// <b>What it checks is structure, not identity:</b> the same node count, and the same node types in the same
    /// drive order. That catches a different graph; it cannot catch a same-shaped graph whose parts were renamed
    /// into other types that happen to line up. A migration between two <i>versions</i> of a graph is the host's
    /// to write — its own store, its own rules for what may change.
    /// </para>
    /// </remarks>
    public static ExecutionCheckpoint Rekey(ExecutionCheckpoint checkpoint, CompiledGraph target)
    {
        if (checkpoint is null) throw new ArgumentNullException(nameof(checkpoint));

        var nodes = NodesOf(target);
        if (nodes.Count != checkpoint.Shape.Count)
        {
            throw new InvalidOperationException(
                $"The checkpoint covers {checkpoint.Shape.Count} nodes and this graph has {nodes.Count}: there is no positional mapping between them.");
        }

        if (checkpoint.Types.Count == nodes.Count)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var type = nodes[i].Node.GetType().Name;
                if (string.Equals(checkpoint.Types[i], type, StringComparison.Ordinal)) continue;
                throw new InvalidOperationException(
                    $"The checkpoint's node {i} is a '{checkpoint.Types[i]}' and this graph's is a '{type}': these are not the same structure.");
            }
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++) map[checkpoint.Shape[i]] = nodes[i].Key;

        var outputs = new Dictionary<string, object?>();
        foreach (var entry in checkpoint.Outputs)
            if (map.TryGetValue(entry.Key, out var key)) outputs[key] = Rekey(entry.Value, map);

        return new ExecutionCheckpoint
        {
            Attempt = checkpoint.Attempt,
            ActiveRedirectTarget = checkpoint.ActiveRedirectTarget,
            Data = Rekey(checkpoint.Data, map),
            Outputs = outputs,
            Shape = [.. nodes.Select(entry => entry.Key)],
            Types = [.. nodes.Select(entry => entry.Node.GetType().Name)],
        };
    }

    // 载荷里的汇合字典是按**节点键**归档的（见 RuntimeContext.Snapshot 里的 Normalize），换了图那些键也要换。
    // 只认「每个键都能在映射表里找到」的字典 —— 那是这类归档的特征；别的字典原样留着。
    private static object? Rekey(object? value, IReadOnlyDictionary<string, string> map)
    {
        if (value is not Dictionary<string, object?> dictionary || dictionary.Count == 0) return value;
        foreach (var key in dictionary.Keys)
            if (!map.ContainsKey(key)) return value;

        var rekeyed = new Dictionary<string, object?>();
        foreach (var entry in dictionary) rekeyed[map[entry.Key]] = Rekey(entry.Value, map);
        return rekeyed;
    }

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
