namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Standard payload passed to <see cref="IWorkflowNodeViewModel.WorkCommand"/>
/// during propagation. Carries the original parameter, plus slot metadata
/// so that the receiving node's helper knows which connection triggered it.
///
/// The source generator's service redirection logic unpacks this interface and
/// forwards its members to <see cref="IWorkflowNodeViewModelHelper.WorkAsync"/>.
/// </summary>
public interface IWorkContext
{
    /// <summary>The original user parameter or context object.</summary>
    object? Parameter { get; }

    /// <summary>The slot that sent the broadcast (output slot of the upstream node).</summary>
    IWorkflowSlotViewModel? Sender { get; }

    /// <summary>The slot that received the broadcast (input slot of the current node).</summary>
    IWorkflowSlotViewModel? Receiver { get; }
}
