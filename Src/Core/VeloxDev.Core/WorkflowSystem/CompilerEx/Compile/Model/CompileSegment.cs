using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Base class for compiled-graph segments: UI / structural state shared by all segments.
/// Concrete segments: <see cref="ChainSegment"/> (linear segment), <see cref="BranchSegment"/> (branch point),
/// <see cref="ParallelSegment"/> (fan-out group).
/// </summary>
public abstract partial class CompileSegment
{
    [VeloxProperty] private Guid _id = Guid.NewGuid();   // Segment UID (UI tree node identifier)
    [VeloxProperty] private int _depth = 0;              // Nesting depth (UI indent)
}
