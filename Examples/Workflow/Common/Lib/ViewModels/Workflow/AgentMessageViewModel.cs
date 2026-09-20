using Newtonsoft.Json.Linq;
using System;
using VeloxDev.MVVM;

namespace Demo.ViewModels;

/// <summary>Role: decides how a message is displayed in the chat transcript (plain text vs. Markdown).</summary>
public enum AgentMessageRole
{
    User,
    Assistant,
    Error,
    Plain,

    /// <summary>
    /// A tool call. Rendered collapsed to a one-line summary — a tool result is a JSON payload, and
    /// printing those inline buries the conversation.
    /// </summary>
    ToolCall,

    /// <summary>
    /// The model's thinking, which the transcript keeps beside its answers rather than folding in. Reached
    /// only through <see cref="FromLogLine"/> — nothing writes it directly.
    /// </summary>
    Reasoning,
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

    /// <summary>Tool calls only: the tool's name.</summary>
    public string ToolName { get; private set; } = string.Empty;

    /// <summary>
    /// Tool calls only: the outcome read from the result's own <c>status</c> field, or <c>null</c> when the
    /// result carries none (several tools answer with a plain payload).
    /// </summary>
    public string? ToolStatus { get; private set; }

    /// <summary>Tool calls only: the full result payload, shown when the row is expanded.</summary>
    public string Detail { get; private set; } = string.Empty;

    /// <summary>Tool calls only: whether the payload is showing. Collapsed by default.</summary>
    [VeloxProperty] private bool isExpanded;

    /// <summary>
    /// The one line a collapsed tool call shows: what was called, and how it went.
    /// </summary>
    public string Summary =>
        Role != AgentMessageRole.ToolCall ? Text
        : ToolStatus is null ? ToolName
        : $"{ToolName}  ·  {ToolStatus}";

    /// <summary>Whether this message has a payload worth expanding into.</summary>
    public bool HasDetail => Role == AgentMessageRole.ToolCall && !string.IsNullOrWhiteSpace(Detail);

    public AgentMessageViewModel(AgentMessageRole role, string text)
    {
        Role = role;
        Text = text;
    }

    /// <summary>
    /// Records a tool call. The status is taken from the result's own <c>status</c> field when it has one —
    /// that is the vocabulary every tool answers in — so the collapsed row says whether the call worked
    /// without the reader having to open it.
    /// </summary>
    public static AgentMessageViewModel ToolCall(string toolName, string result)
        => new(AgentMessageRole.ToolCall, string.Empty)
        {
            ToolName = toolName,
            ToolStatus = ReadStatus(result),
            Detail = result ?? string.Empty,
        };

    /// <summary>A streaming reply grows its text in place, so the line a template binds must follow it.</summary>
    partial void OnTextChanged(string oldValue, string newValue) => OnPropertyChanged(nameof(Summary));

    /// <summary>Reads a tool result's <c>status</c>, or returns <c>null</c> if it does not carry one.</summary>
    private static string? ReadStatus(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        try
        {
            return JObject.Parse(result!)["status"]?.ToString();
        }
        catch (Exception)
        {
            // Not every tool answers with JSON, and a truncated or plain-text result is not an error here.
            return null;
        }
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
        // The transcript renders reasoning as "[Thinking] …", and a host that shows the thinking has to
        // recognise it here or the line lands as an anonymous Plain message.
        if (trimmed.StartsWith("[Thinking]", StringComparison.Ordinal))
            return new AgentMessageViewModel(AgentMessageRole.Reasoning, trimmed.Substring("[Thinking]".Length).TrimStart());

        return new AgentMessageViewModel(AgentMessageRole.Plain, trimmed);
    }
}
