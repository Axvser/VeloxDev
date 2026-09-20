using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using VeloxDev.MVVM;

namespace VeloxDev.AI.Pipelines;

/// <summary>What an entry in the conversation is.</summary>
public enum AgentTranscriptRole
{
    /// <summary>Something the user said.</summary>
    User,

    /// <summary>The answer, as the user reads it.</summary>
    Assistant,

    /// <summary>The model's thinking, shown separately from the answer.</summary>
    Reasoning,

    /// <summary>A tool the agent used.</summary>
    ToolCall,

    /// <summary>The run failed.</summary>
    Error,
}

/// <summary>
/// One entry in the conversation.
/// <para>
/// A single type carrying a role rather than a class per role, matching the shape hosts already render:
/// a template binds <see cref="Role"/> and picks its own presentation, and a text-only host just prints
/// <see cref="Text"/>.
/// </para>
/// </summary>
public partial class AgentTranscriptEntry
{
    /// <summary>
    /// The entry's text. Grows in place while a streamed answer arrives — a host binding it sees the
    /// message build up rather than being handed a new object per fragment.
    /// </summary>
    [VeloxProperty] private string text = string.Empty;

    private AgentTranscriptEntry(AgentTranscriptRole role, string text)
    {
        Role = role;
        Text = text;
    }

    /// <summary>What this entry is.</summary>
    public AgentTranscriptRole Role { get; }

    /// <summary>When it was first created.</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;

    /// <summary>Tool name, for <see cref="AgentTranscriptRole.ToolCall"/>.</summary>
    public string ToolName { get; private set; } = string.Empty;

    /// <summary>How the tool ended, for <see cref="AgentTranscriptRole.ToolCall"/>.</summary>
    public AgentToolOutcome? Outcome { get; private set; }

    /// <summary>The tool's raw result payload, for a host that wants to show it expanded.</summary>
    public string Detail { get; private set; } = string.Empty;

    /// <summary>
    /// Whether more fragments may be appended. Streamed answer and reasoning entries stay open until
    /// something else arrives; every other kind is complete the moment it is created.
    /// </summary>
    public bool IsStreaming => Role is AgentTranscriptRole.Assistant or AgentTranscriptRole.Reasoning;

    /// <summary>One collapsed line — what a compact host shows without expanding the payload.</summary>
    public string Summary => Role switch
    {
        AgentTranscriptRole.ToolCall => Outcome is null ? ToolName : $"{ToolName} · {Outcome}",
        _ => Text,
    };

    /// <summary>Whether there is a payload worth expanding into.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>A message from the user.</summary>
    public static AgentTranscriptEntry User(string text) => new(AgentTranscriptRole.User, text);

    /// <summary>A new, empty answer — fragments are appended as they arrive.</summary>
    public static AgentTranscriptEntry Assistant() => new(AgentTranscriptRole.Assistant, string.Empty);

    /// <summary>A new, empty reasoning block.</summary>
    public static AgentTranscriptEntry Reasoning() => new(AgentTranscriptRole.Reasoning, string.Empty);

    /// <summary>An error, rendered to the user.</summary>
    public static AgentTranscriptEntry Error(string text) => new(AgentTranscriptRole.Error, text);

    /// <summary>A completed tool call.</summary>
    public static AgentTranscriptEntry ToolCall(string toolName, string result, AgentToolOutcome outcome)
        => new(AgentTranscriptRole.ToolCall, string.Empty)
        {
            ToolName = toolName ?? string.Empty,
            Detail = result ?? string.Empty,
            Outcome = outcome,
        };

    /// <summary>
    /// Appends a fragment. Called by <see cref="AgentTranscript"/> while an entry is open.
    /// <para>
    /// Concatenated on arrival, so <see cref="Text"/> is always the whole answer so far: a host binds it
    /// directly, and deferring the concatenation to render time would leave that binding empty until
    /// something happened to read the transcript. The repeated copy is real but small — a 20 KB answer
    /// arriving in fragments costs a few tens of milliseconds in total — and nowhere near the cost of
    /// re-rendering the panel, which is throttled by the host instead.
    /// </para>
    /// </summary>
    internal void Append(string fragment)
    {
        if (string.IsNullOrEmpty(fragment)) return;
        Text += fragment;
    }
}

/// <summary>
/// How <see cref="AgentTranscript.ToMarkdown"/> shapes its output. Every member has a shipped default, so a
/// host that wants the stock rendering passes nothing at all.
/// </summary>
public sealed class AgentMarkdownOptions
{
    /// <summary>
    /// The info string on the fence the reasoning is wrapped in — <c>thinking</c> by default. An empty
    /// string drops the fence and restores the bold paragraph it replaced.
    /// </summary>
    public string ReasoningFence { get; set; } = "thinking";

    /// <summary>
    /// The label above that fence, or <c>null</c> for none. Kept by default because it is what the panel
    /// showed before the fence existed; a host that would rather the block stands alone nulls it.
    /// </summary>
    public string? ReasoningHeading { get; set; } = "**思考：**";
}

/// <summary>
/// The conversation a host binds: what the user said, what the model thought, what it answered, and which
/// tools it used — in one ordered collection.
/// <para>
/// <b>This type owns the two rules the demo host used to implement itself, and got wrong.</b>
/// </para>
/// <para>
/// It was never implemented per platform: the run loop lived in one shared file, so the same defect showed
/// up wherever that file's output was rendered. The problem was that it was host code — a view model in the
/// demo — which is exactly why it could not be tested and kept regressing.
/// </para>
/// <list type="number">
///   <item>
///     <b>A fragment continues the open entry only when that entry has the same role.</b> Anything else in
///     between — a tool call, a reasoning block, the user — closes it, and the answer resumes in a new
///     entry. The old host code appended only when the last message was still an assistant message, so once
///     a tool call landed every later fragment of that reply went to a side log and was never rendered.
///   </item>
///   <item>
///     <b>Tool calls are entries, not separators.</b> A tool call is a row with a name and an outcome, so a
///     renderer never has to emit a divider for something that has no text. The old markdown path appended
///     the (empty) text of a tool-call row, which produced the column of blank rules.
///   </item>
/// </list>
/// <para>
/// Not thread-safe by design: one pipeline stage feeds it, on whatever thread that stage is marshalled to.
/// </para>
/// </summary>
public partial class AgentTranscript
{
    [VeloxProperty] private ObservableCollection<AgentTranscriptEntry> entries = [];

    /// <summary>The conversation, oldest first.</summary>
    // Entries is generated from the field above.

    /// <summary>
    /// The entry fragments are currently being appended to, or <c>null</c> when nothing is open.
    /// </summary>
    public AgentTranscriptEntry? Open { get; private set; }

    /// <summary>
    /// Closes the open entry without starting another. Called when a turn ends, so the next turn's first
    /// fragment begins its own entry instead of being appended to a bubble nobody is writing to any more.
    /// </summary>
    public void CloseOpen() => Open = null;

    /// <summary>Drops the whole conversation.</summary>
    public void Clear()
    {
        Open = null;
        Entries.Clear();
    }

    /// <summary>Records something the user said, closing any open answer.</summary>
    public AgentTranscriptEntry AddUser(string text)
    {
        var entry = AgentTranscriptEntry.User(text);
        Close(entry);
        return entry;
    }

    /// <summary>Records a failure, closing any open answer.</summary>
    public AgentTranscriptEntry AddError(string text)
    {
        var entry = AgentTranscriptEntry.Error(text);
        Close(entry);
        return entry;
    }

    /// <summary>
    /// Appends a fragment of the answer. Extends the open answer while nothing else has happened; a tool
    /// call, a reasoning block or a new user message closes it, and the answer resumes in a new entry
    /// rather than being lost.
    /// </summary>
    public AgentTranscriptEntry AppendAnswer(string fragment)
        => Append(AgentTranscriptRole.Assistant, fragment);

    /// <summary>Appends a fragment of the model's reasoning. Same continuation rule as the answer.</summary>
    public AgentTranscriptEntry AppendReasoning(string fragment)
        => Append(AgentTranscriptRole.Reasoning, fragment);

    /// <summary>Records a finished tool call, closing any open answer first.</summary>
    public AgentTranscriptEntry AddToolCall(string toolName, string result, AgentToolOutcome outcome)
    {
        var entry = AgentTranscriptEntry.ToolCall(toolName, result, outcome);
        Close(entry);
        return entry;
    }

    private AgentTranscriptEntry Append(AgentTranscriptRole role, string fragment)
    {
        // The whole rule: continue only a same-role entry that is still open, otherwise begin a new one.
        if (Open is null || Open.Role != role || !Open.IsStreaming)
        {
            Open = role == AgentTranscriptRole.Assistant
                ? AgentTranscriptEntry.Assistant()
                : AgentTranscriptEntry.Reasoning();
            Entries.Add(Open);
        }

        Open.Append(fragment);
        return Open;
    }

    /// <summary>Adds a completed entry and closes the open one.</summary>
    private void Close(AgentTranscriptEntry entry)
    {
        Open = null;
        Entries.Add(entry);
    }

    /// <summary>
    /// Renders the conversation as Markdown, with consecutive tool calls gathered into one block.
    /// <para>
    /// Provided so that the markdown hosts do not each write their own renderer — which is where the blank
    /// separator rows came from.
    /// </para>
    /// <para>
    /// Reasoning is wrapped in a fenced code block rather than a bold paragraph, so a Markdown control draws
    /// it as a block of its own instead of as the answer's shape under a different label. The fence is
    /// measured against the body: the model's thinking about code contains backtick runs, and a fixed
    /// three-backtick fence would be closed by the first one it met. The label stays above the fence, where
    /// it is a paragraph rather than code.
    /// </para>
    /// <para>
    /// The shape is <paramref name="options"/>'s to decide; passing nothing renders the shape described here.
    /// </para>
    /// </summary>
    /// <param name="options">
    /// Overrides the reasoning fence and its label. <c>null</c> — the default — uses the shipped shape.
    /// </param>
    public string ToMarkdown(AgentMarkdownOptions? options = null)
    {
        options ??= new AgentMarkdownOptions();

        var sb = new StringBuilder();
        var toolRun = new List<AgentTranscriptEntry>();

        void FlushTools()
        {
            if (toolRun.Count == 0) return;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append("**工具调用：**\n\n");
            foreach (var tool in toolRun)
            {
                sb.Append("- `").Append(tool.ToolName).Append('`');
                if (tool.Outcome is { } outcome) sb.Append(" · ").Append(outcome);
                sb.Append('\n');
            }
            toolRun.Clear();
        }

        foreach (var entry in Entries)
        {
            if (entry.Role == AgentTranscriptRole.ToolCall)
            {
                toolRun.Add(entry);
                continue;
            }

            FlushTools();

            if (sb.Length > 0) sb.Append("\n\n---\n\n");

            switch (entry.Role)
            {
                case AgentTranscriptRole.User:
                    sb.Append("**你：** ").Append(entry.Text);
                    break;
                case AgentTranscriptRole.Assistant:
                    sb.Append("**助手：**\n\n").Append(entry.Text);
                    break;
                case AgentTranscriptRole.Reasoning:
                    AppendReasoning(sb, options, entry.Text);
                    break;
                case AgentTranscriptRole.Error:
                    sb.Append("**错误：** ").Append(entry.Text);
                    break;
                default:
                    sb.Append(entry.Text);
                    break;
            }
        }

        // A turn that ends on tool calls has no following message to flush them for.
        FlushTools();

        return sb.ToString();
    }

    /// <summary>
    /// Renders the conversation as one line per entry, for hosts with no rich text.
    /// <para>
    /// Tool calls appear here too — the plain-text hosts used to show none at all, because tool calls only
    /// ever reached the structured list.
    /// </para>
    /// <para>
    /// <b>One entry is one line, and that is a contract rather than a shape.</b> The demo host syncs these
    /// into its log with a count comparison and a sequence comparison, and it parses the prefixes back into
    /// roles; splitting an entry, or renaming a prefix, changes what it displays. A host that wants another
    /// arrangement builds it from <see cref="Entries"/> instead.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ToPlainTextLines()
    {
        var lines = new List<string>(Entries.Count);
        foreach (var entry in Entries)
        {
            switch (entry.Role)
            {
                case AgentTranscriptRole.User:
                    lines.Add($"[User] {entry.Text}");
                    break;
                case AgentTranscriptRole.Assistant:
                    lines.Add($"[Agent] {entry.Text}");
                    break;
                case AgentTranscriptRole.Reasoning:
                    lines.Add($"[Thinking] {entry.Text}");
                    break;
                case AgentTranscriptRole.ToolCall:
                    lines.Add($"    🔧 {entry.Summary}");
                    break;
                case AgentTranscriptRole.Error:
                    lines.Add($"[Error] {entry.Text}");
                    break;
            }
        }
        return lines;
    }

    /// <summary>
    /// Writes one reasoning block: the label as its own paragraph, then the thinking — fenced when
    /// <see cref="AgentMarkdownOptions.ReasoningFence"/> names a language.
    /// </summary>
    private static void AppendReasoning(StringBuilder sb, AgentMarkdownOptions options, string text)
    {
        if (!string.IsNullOrEmpty(options.ReasoningHeading))
            sb.Append(options.ReasoningHeading).Append("\n\n");

        var info = FenceInfo(options.ReasoningFence);
        if (info.Length == 0)
        {
            sb.Append(text);
            return;
        }

        // The fence is measured against the body, so no line inside it can be read as the closing fence.
        var fence = new string('`', FenceLength(text));
        sb.Append(fence).Append(info).Append('\n');
        sb.Append(text);
        if (sb[sb.Length - 1] != '\n') sb.Append('\n');
        sb.Append(fence);
    }

    /// <summary>
    /// The fence length a body needs: longer than every backtick run inside it.
    /// <para>
    /// CommonMark closes a fenced block at the first line whose backtick run is at least as long as the
    /// opener's, and thinking about code is full of backtick runs. A fixed three would be closed by the
    /// first one the model wrote, swallowing everything that followed into the code block.
    /// </para>
    /// </summary>
    private static int FenceLength(string body)
    {
        var longest = 0;
        var run = 0;
        for (var i = 0; i < body.Length; i++)
        {
            run = body[i] == '`' ? run + 1 : 0;
            if (run > longest) longest = run;
        }
        return Math.Max(3, longest + 1);
    }

    /// <summary>
    /// The info string to put on the fence. Backticks are dropped: one would merge with the fence and
    /// lengthen the opening run past the closing one, leaving the block open for the rest of the document.
    /// </summary>
    private static string FenceInfo(string? fence) => fence?.Replace("`", string.Empty) ?? string.Empty;
}
