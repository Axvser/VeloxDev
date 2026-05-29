namespace VeloxDev.AI;

/// <summary>
/// Provides data for the <see cref="IAgentConfirmationNotifier.ConfirmationRequested"/> event,
/// raised when an Agent needs explicit user approval before executing a sensitive operation.
/// </summary>
/// <param name="operationKey">Stable identifier for the operation type.</param>
/// <param name="description">Human-readable description shown in the confirmation dialog.</param>
public sealed class AgentConfirmationEventArgs(string operationKey, string description) : EventArgs
{
    /// <summary>
    /// A stable, machine-readable key that uniquely identifies the type of operation
    /// being confirmed (e.g. <c>"DeleteNode"</c>, <c>"ClearWorkflow"</c>).
    /// Used to persist session-wide approvals via <see cref="AgentConfirmationResult.AllowAlways"/>.
    /// </summary>
    public string OperationKey { get; } = operationKey ?? throw new ArgumentNullException(nameof(operationKey));

    /// <summary>
    /// A human-readable explanation of what the operation will do, shown to the user
    /// as the body of the confirmation dialog.
    /// </summary>
    public string Description { get; } = description ?? throw new ArgumentNullException(nameof(description));

    /// <summary>
    /// The user's decision. Set this inside a
    /// <see cref="IAgentConfirmationNotifier.ConfirmationRequested"/> handler
    /// before signalling completion.
    /// Defaults to <see cref="AgentConfirmationResult.Deny"/>.
    /// </summary>
    public AgentConfirmationResult Result { get; set; } = AgentConfirmationResult.Deny;
}

/// <summary>
/// Contract for any Agent scope or toolkit that requires explicit user approval
/// before executing sensitive operations via the <c>RequestConfirmation</c> interaction tool.
/// </summary>
public interface IAgentConfirmationNotifier
{
    /// <summary>
    /// Raised when the Agent invokes the <c>RequestConfirmation</c> tool.
    /// Subscribers must set <see cref="AgentConfirmationEventArgs.Result"/>
    /// and signal any awaitable completion mechanism before returning.
    /// </summary>
    event EventHandler<AgentConfirmationEventArgs> ConfirmationRequested;
}
