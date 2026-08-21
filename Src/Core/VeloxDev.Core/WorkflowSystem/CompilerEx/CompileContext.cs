using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The compile-time context: the compile identity assigned to each node during compilation.
/// Global order is monotonically continuous: a downstream branch's Graph start carries an offset (not reset to
/// zero); -1 means the absolute stop state (unselected branch / termination).
/// </summary>
public sealed partial class CompileContext : ICompileContext
{
    /// <summary>Compile-time identity, always true (static-check phase).</summary>
    public bool IsCompilePhase => true;

    /// <summary>No data payload at compile-time, always null.</summary>
    public object? Data => null;

    /// <summary>Sender output slot of the edge to validate; null on the identity instance held by the node itself, only filled in on edge instances constructed by the compiler.</summary>
    public IWorkflowSlotViewModel? Sender { get; set; }

    /// <summary>Receiver input slot of the edge to validate; null on the identity instance held by the node itself, only filled in on edge instances constructed by the compiler.</summary>
    public IWorkflowSlotViewModel? Receiver { get; set; }

    /// <summary>Join-point input source nodes (registered at compile-time; aggregated into GroupData injected as Data when Count &gt; 1 at runtime). Null on non-join nodes.</summary>
    public IReadOnlyList<IWorkflowNodeViewModel>? InputNodes { get; set; }

    [VeloxProperty] private int _order = -1;         // Global computed order; -1 = absolute stop state
    [VeloxProperty] private int _chainIndex = -1;    // Order within its chain (starting at 0)
    [VeloxProperty] private int _offset = 0;         // Entry offset for this graph (Router downstream > 0)
}
