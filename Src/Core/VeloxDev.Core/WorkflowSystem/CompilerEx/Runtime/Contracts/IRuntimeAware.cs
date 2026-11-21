using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// Optional runtime notification: before each drive the engine hands the node the current run's
/// <see cref="IRuntimeContext"/>.
/// </summary>
/// <remarks>
/// The same object is already the parameter <see cref="IWorkflowNodeViewModelHelper.ReceiveAsync"/> receives, so a
/// node that does not implement this interface loses nothing — it can keep that argument itself. Implement it only
/// when the node wants the session outside a drive (for example to bind run progress to its own UI). An exception
/// thrown here is logged and the drive continues: a notification cannot change where the run goes.
/// </remarks>
public interface IRuntimeAware
{
    /// <summary>Receives the session driving this node, once immediately before each drive.</summary>
    /// <param name="context">The run session.</param>
    void AttachRuntimeContext(IRuntimeContext context);
}
