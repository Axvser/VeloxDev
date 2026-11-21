using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The compile identity the compiler assigns to one node: its global order, in-chain index, entry offset, and the
/// upstream sources a join point aggregates.
/// </summary>
/// <remarks>
/// The compiler owns this record. It is stored in the graph's binding table
/// (<see cref="CompiledGraph.TryGetCompileInfo"/>), never on the node; a node implementing
/// <see cref="ICompileTimeAware"/> receives a <see cref="CompileContext"/> copy as an optional notification only.
/// </remarks>
public sealed class NodeCompileInfo
{
    internal NodeCompileInfo(
        int order, int chainIndex, int offset, IReadOnlyList<INodeComponent>? inputNodes)
    {
        Order = order;
        ChainIndex = chainIndex;
        Offset = offset;
        InputNodes = inputNodes;
    }

    /// <summary>Global execution order; <c>-1</c> is the absolute stop state.</summary>
    public int Order { get; }

    /// <summary>Index within the linear segment.</summary>
    public int ChainIndex { get; }

    /// <summary>Entry offset of the sub-graph this node belongs to.</summary>
    public int Offset { get; }

    /// <summary>
    /// The upstream sources a join point aggregates into <see cref="IGroupData"/>; <see langword="null"/> on chained
    /// and single-input nodes.
    /// </summary>
    public IReadOnlyList<INodeComponent>? InputNodes { get; }
}
