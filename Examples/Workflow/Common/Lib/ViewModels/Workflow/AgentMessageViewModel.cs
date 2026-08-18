using VeloxDev.MVVM;

namespace Demo.ViewModels;

/// <summary>角色：用来决定聊天记录里一条消息的展示方式（纯文本还是 Markdown）。</summary>
public enum AgentMessageRole
{
    User,
    Assistant,
    Error,
    Plain,
}

/// <summary>
/// 结构化 Agent 聊天消息。与 <see cref="TreeViewModel.AgentLog"/>（纯文本行）并存：
/// <see cref="AgentLog"/> 供各平台 Demo 兼容使用，<see cref="TreeViewModel.AgentMessages"/>
/// 供 Avalonia Full Demo 以 AvalonMarkdown 渲染助手回复中的 Markdown 内容。
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

    /// <summary>从兼容的纯文本日志行（带 emoji 前缀）还原为结构化消息。</summary>
    public static AgentMessageViewModel FromLogLine(string line)
    {
        var trimmed = line?.TrimStart() ?? string.Empty;

        if (trimmed.StartsWith("🧑", StringComparison.Ordinal))
            return new AgentMessageViewModel(AgentMessageRole.User, trimmed.Substring("🧑".Length).TrimStart());
        if (trimmed.StartsWith("🤖", StringComparison.Ordinal))
            return new AgentMessageViewModel(AgentMessageRole.Assistant, trimmed.Substring("🤖".Length).TrimStart());
        if (trimmed.StartsWith("❌", StringComparison.Ordinal))
            return new AgentMessageViewModel(AgentMessageRole.Error, trimmed.Substring("❌".Length).TrimStart());

        return new AgentMessageViewModel(AgentMessageRole.Plain, trimmed);
    }
}
