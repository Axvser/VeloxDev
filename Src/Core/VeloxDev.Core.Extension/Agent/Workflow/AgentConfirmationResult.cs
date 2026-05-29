namespace VeloxDev.AI.Workflow;

/// <summary>
/// Represents the result of a user-facing confirmation dialog raised by the Agent
/// via the <c>RequestConfirmation</c> tool.
/// </summary>
public enum AgentConfirmationResult
{
    /// <summary>
    /// The user denied the operation. The Agent should not proceed.
    /// </summary>
    Deny,

    /// <summary>
    /// The user allows the operation this one time only.
    /// The next identical request will trigger the dialog again.
    /// </summary>
    AllowOnce,

    /// <summary>
    /// The user allows the operation for the rest of the current session.
    /// Subsequent requests with the same <c>operationKey</c> are silently approved.
    /// </summary>
    AllowAlways,
}
