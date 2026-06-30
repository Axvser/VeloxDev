namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Standard payload passed to <see cref="IWorkflowNodeViewModel.WorkCommand"/>
/// during propagation. Carries the original parameter, plus slot metadata
/// so that the receiving node's helper knows which connection triggered it.
///
/// The source generator's service redirection logic unpacks this struct and
/// forwards its members to <see cref="IWorkflowNodeViewModelHelper.WorkAsync"/>.
/// </summary>
public readonly struct WorkContext
{
    /// <summary>The original user parameter or context object.</summary>
    public object? Parameter { get; }

    /// <summary>The slot that sent the broadcast (output slot of the upstream node).</summary>
    public IWorkflowSlotViewModel? Sender { get; }

    /// <summary>The slot that received the broadcast (input slot of the current node).</summary>
    public IWorkflowSlotViewModel? Receiver { get; }

    public WorkContext(object? parameter,
        IWorkflowSlotViewModel? sender = null,
        IWorkflowSlotViewModel? receiver = null)
    {
        Parameter = parameter;
        Sender = sender;
        Receiver = receiver;
    }

    public void Deconstruct(out object? parameter,
        out IWorkflowSlotViewModel? sender,
        out IWorkflowSlotViewModel? receiver)
    {
        parameter = Parameter;
        sender = Sender;
        receiver = Receiver;
    }
}
