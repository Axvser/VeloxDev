namespace VeloxDev.AI;

/// <summary>
/// Provides data for the <see cref="IAgentSelectionNotifier.SelectionRequested"/> event,
/// raised when an Agent needs the user to choose one option from a list.
/// </summary>
/// <param name="prompt">Human-readable question or instruction shown above the option list.</param>
/// <param name="options">Non-empty array of candidate strings.</param>
public sealed class AgentSelectionEventArgs(string prompt, string[] options) : EventArgs
{
    /// <summary>
    /// The prompt presented to the user, explaining what they are choosing between.
    /// </summary>
    public string Prompt { get; } = prompt ?? throw new ArgumentNullException(nameof(prompt));

    /// <summary>
    /// The ordered list of options the user may select from.
    /// </summary>
    public IReadOnlyList<string> Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// The option chosen by the user, or <c>null</c> if the selection was cancelled.
    /// Set this inside a <see cref="IAgentSelectionNotifier.SelectionRequested"/> handler
    /// before signalling completion.
    /// </summary>
    public string? SelectedOption { get; set; }
}

/// <summary>
/// Contract for any Agent scope or toolkit that can ask the user to pick one item
/// from a list via the <c>RequestSelection</c> interaction tool.
/// </summary>
public interface IAgentSelectionNotifier
{
    /// <summary>
    /// Raised when the Agent invokes the <c>RequestSelection</c> tool.
    /// Subscribers must set <see cref="AgentSelectionEventArgs.SelectedOption"/>
    /// and signal any awaitable completion mechanism before returning.
    /// </summary>
    event EventHandler<AgentSelectionEventArgs> SelectionRequested;
}
