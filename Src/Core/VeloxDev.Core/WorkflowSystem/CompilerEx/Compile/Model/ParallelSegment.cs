using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// A fan-out group: when one branch condition feeds multiple downstream targets, all child graphs are wrapped into
/// one group. Executing each branch sequentially satisfies the join semantics of "waiting for all upstreams to
/// arrive" (the shared IRuntimeContext blackboard is not thread-safe, so no true parallelism).
/// </summary>
public sealed partial class ParallelSegment : CompileSegment
{
    /// <summary>Each fan-out child graph (executed sequentially).</summary>
    [VeloxProperty] private ObservableCollection<CompiledGraph> _branches = [];
}
