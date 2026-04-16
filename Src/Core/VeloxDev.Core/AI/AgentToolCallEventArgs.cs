using System;

namespace VeloxDev.AI;

/// <summary>
/// Provides data for the <see cref="IAgentToolCallNotifier.ToolCalled"/> event,
/// raised after each Agent tool invocation.
/// </summary>
public class AgentToolCallEventArgs : EventArgs
{
    /// <summary>
    /// Name of the tool that was invoked.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// JSON result returned by the tool.
    /// </summary>
    public string Result { get; }

    /// <summary>
    /// Cumulative number of tool calls in the current session.
    /// </summary>
    public int CallCount { get; }

    public AgentToolCallEventArgs(string toolName, string result, int callCount)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Result = result ?? string.Empty;
        CallCount = callCount;
    }
}

/// <summary>
/// Contract for any toolkit or scope that can notify subscribers when an Agent tool is called.
/// </summary>
public interface IAgentToolCallNotifier
{
    /// <summary>
    /// Raised after each Agent tool invocation completes.
    /// </summary>
    event EventHandler<AgentToolCallEventArgs> ToolCalled;
}
