using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The compile-time identity context interface. Implemented by <see cref="CompileContext"/>; the authoritative
/// copy is held by the compiled graph (<see cref="CompiledGraph.TryGetCompileInfo"/>), and
/// <see cref="ICompileTimeAware.AttachCompileTimeContext"/> receives a snapshot as an optional notification.
/// </summary>
public interface ICompileContext : IAccessContext
{
    /// <summary>Compile-time fixed execution order (-1 = absolute stop).</summary>
    int Order { get; set; }

    /// <summary>Index within the linear segment.</summary>
    int ChainIndex { get; set; }

    /// <summary>Sub-graph entry offset.</summary>
    int Offset { get; set; }

    /// <summary>
    /// Join-point input source nodes (registered from each branch's exit at compile-time). When Count &gt; 1,
    /// the runtime aggregates each upstream output into a <see cref="GroupData"/> injected as
    /// <see cref="IRuntimeContext.Data"/>; null on chained/single-input nodes, preserving bare Data chaining
    /// semantics.
    /// </summary>
    IReadOnlyList<INodeComponent>? InputNodes { get; set; }
}
