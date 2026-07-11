namespace VeloxDev.AI;

/// <summary>
/// Provides data for the <see cref="IAgentSelectionNotifier.SelectionRequested"/> event,
/// raised when an Agent needs the user to choose one or more options from a list.
/// A free-text input field is always shown below the options.
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
    /// When <c>true</c>, the user may select multiple options (checkboxes).
    /// When <c>false</c> (default), the user selects exactly one option (radio-buttons).
    /// </summary>
    public bool AllowMultiSelect { get; set; }

    /// <summary>
    /// Label shown above the free-text input field. Provided by the Agent in the
    /// appropriate output language (configured via <c>WithOutputLanguage</c>).
    /// </summary>
    public string FreeTextPrompt { get; set; } = "自定义输入（可选）";

    /// <summary>
    /// For single-select mode (<see cref="AllowMultiSelect"/> is <c>false</c>):
    /// the option chosen by the user, or <c>null</c> if the selection was cancelled.
    /// Set this inside a handler before signalling completion.
    /// </summary>
    public string? SelectedOption { get; set; }

    /// <summary>
    /// For multi-select mode (<see cref="AllowMultiSelect"/> is <c>true</c>):
    /// the options chosen by the user. When empty, no predefined option was selected.
    /// Set this inside a handler before signalling completion.
    /// </summary>
    public IReadOnlyList<string>? SelectedOptions { get; set; }

    /// <summary>
    /// The text the user typed into the free-text input field.
    /// When <c>null</c> or empty, no free-text input was provided.
    /// Set this inside a handler before signalling completion.
    /// </summary>
    public string? FreeTextResponse { get; set; }
}

/// <summary>
/// Contract for any Agent scope or toolkit that can ask the user to pick one or more items
/// from a list via the <c>RequestSelection</c> interaction tool.
/// </summary>
public interface IAgentSelectionNotifier
{
    /// <summary>
    /// Raised when the Agent invokes the <c>RequestSelection</c> tool.
    /// Subscribers must set <see cref="AgentSelectionEventArgs.SelectedOption"/> (single)
    /// or <see cref="AgentSelectionEventArgs.SelectedOptions"/> (multi) and/or
    /// <see cref="AgentSelectionEventArgs.FreeTextResponse"/>,
    /// then signal any awaitable completion mechanism before returning.
    /// </summary>
    event EventHandler<AgentSelectionEventArgs> SelectionRequested;
}
