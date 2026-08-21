using VeloxDev.MVVM;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Base class for execution entries: UI / structural state shared by all entries.
/// Concrete entries: <see cref="ExecuteEntry"/> (linear segment), <see cref="BranchEntry"/> (branch point),
/// <see cref="ParallelEntry"/> (fan-out group).
/// </summary>
public abstract partial class ActionEntry
{
    [VeloxProperty] private Guid _id = Guid.NewGuid();   // Entry UID (UI tree node identifier)
    [VeloxProperty] private int _depth = 0;              // Nesting depth (UI indent)
}
