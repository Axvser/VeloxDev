using VeloxDev.MVVM;

namespace Demo.ViewModels;

/// <summary>Role: decides how a message is displayed in the chat transcript (plain text vs. Markdown).</summary>
public enum AgentMessageRole
{
    User,
    Assistant,
    Error,
    Plain,
}

/// <summary>
/// Structured agent chat message. It coexists with <see cref="TreeViewModel.AgentLog"/> (plain-text lines):
/// <see cref="AgentLog"/> is used across platform demos for compatibility, while
/// <see cref="TreeViewModel.AgentMessages"/> lets the Avalonia Full Demo render Markdown content in the
/// assistant's replies with AvalonMarkdown.
/// </summary>
public partial class AgentMessageViewModel
{
    public AgentMessageRole Role { get; }

    [VeloxProperty] private string text = "";

    public AgentMessageViewModel(AgentMessageRole role, string text)
    {
        Role = role;
        Text = text;
    }

    /// <summary>Restores a structured message from a compatible plain-text log line (with a role-marker prefix).</summary>
    public static AgentMessageViewModel FromLogLine(string line)
    {
        var trimmed = line?.TrimStart() ?? string.Empty;

        if (trimmed.StartsWith("[User]", StringComparison.Ordinal))
            return new AgentMessageViewModel(AgentMessageRole.User, trimmed.Substring("[User]".Length).TrimStart());
        if (trimmed.StartsWith("[Agent]", StringComparison.Ordinal))
            return new AgentMessageViewModel(AgentMessageRole.Assistant, trimmed.Substring("[Agent]".Length).TrimStart());
        if (trimmed.StartsWith("[Error]", StringComparison.Ordinal))
            return new AgentMessageViewModel(AgentMessageRole.Error, trimmed.Substring("[Error]".Length).TrimStart());

        return new AgentMessageViewModel(AgentMessageRole.Plain, trimmed);
    }
}
