using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// One branch option: key + label + downstream sub-graph.
/// All branches of a dynamic branch stay alive (selected by key at runtime).
/// <see cref="IsTerminal"/>: the branch registers a route key but has no downstream node — selecting it at runtime
/// ends the whole run (the join-point tail segment is not propagated).
/// </summary>
public sealed partial class BranchOption
{
    [VeloxProperty] private object? _key;
    [VeloxProperty] private string? _label;
    [VeloxProperty] private CompiledGraph? _graph;
    [VeloxProperty] private bool _isTerminal;
}
