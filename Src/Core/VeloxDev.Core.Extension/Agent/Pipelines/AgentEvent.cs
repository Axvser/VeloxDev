using System;

namespace VeloxDev.AI.Pipelines;

/// <summary>
/// One thing that happened inside an agent run.
/// <para>
/// This is the vocabulary the pipeline speaks. The demo host used to reconstruct it by hand — it looped
/// over <c>RunStreamingAsync</c> itself, read only <c>update.Text</c>, re-split it on sentence breaks and
/// pushed the pieces into its own chat list. Because that lived in host code rather than here, the same
/// three defects kept reappearing wherever it was rendered: text dropped after a tool call, tool calls
/// rendering as blank rows, and reasoning thrown away because <c>Text</c> never carried it.
/// </para>
/// <para>
/// Events are observations, not control flow: a stage may drop or rewrite them, but the run itself is
/// driven by the agent the pipeline is attached to.
/// </para>
/// </summary>
public abstract class AgentEvent
{
    /// <summary>When the pipeline observed this.</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
}

/// <summary>A run began. Carries the prompt that started it.</summary>
public sealed class AgentTurnStarted(AgentRunKind kind, string? prompt) : AgentEvent
{
    /// <summary>Whether this came from <c>RunAsync</c> or <c>RunStreamingAsync</c>.</summary>
    public AgentRunKind Kind { get; } = kind;

    /// <summary>The user message that started the run, when one was passed.</summary>
    public string? Prompt { get; } = prompt;
}

/// <summary>A run finished. Reported for the streaming and non-streaming paths alike.</summary>
public sealed class AgentTurnCompleted(AgentRunKind kind, string? text, TimeSpan elapsed) : AgentEvent
{
    /// <summary>Whether this came from <c>RunAsync</c> or <c>RunStreamingAsync</c>.</summary>
    public AgentRunKind Kind { get; } = kind;

    /// <summary>
    /// The run's visible text as the agent reported it — populated on the non-streaming path, and
    /// <c>null</c> when streaming, where the text arrives as <see cref="AgentTextDelta"/> instead.
    /// </summary>
    public string? Text { get; } = text;

    /// <summary>Wall-clock duration of the run.</summary>
    public TimeSpan Elapsed { get; } = elapsed;
}

/// <summary>
/// A run ended badly — an exception, or cancellation.
/// <para>
/// Cancellation is reported separately from failure because a host that cancelled on purpose should not
/// render an error, and a pipeline that collapsed the two would leave it no way to tell.
/// </para>
/// </summary>
public sealed class AgentTurnFaulted(AgentRunKind kind, Exception? error, bool cancelled) : AgentEvent
{
    /// <summary>Whether this came from <c>RunAsync</c> or <c>RunStreamingAsync</c>.</summary>
    public AgentRunKind Kind { get; } = kind;

    /// <summary>The failure, or <c>null</c> when the run was cancelled.</summary>
    public Exception? Error { get; } = error;

    /// <summary>True when the run ended because its token was signalled, not because it failed.</summary>
    public bool Cancelled { get; } = cancelled;
}

/// <summary>
/// A fragment of the answer the user is meant to read.
/// <para>
/// Deliberately a raw fragment rather than a whole sentence: the splitting the demos used to do was lossy
/// (it could sit on a fragment until the next punctuation mark, and dropped anything after a tool call),
/// and where a message ends is <see cref="AgentTranscript"/>'s decision, not the model's punctuation.
/// </para>
/// </summary>
public sealed class AgentTextDelta(string text) : AgentEvent
{
    /// <summary>The fragment.</summary>
    public string Text { get; } = text;
}

/// <summary>
/// A fragment of the model's reasoning — the thinking it did before answering.
/// <para>
/// Recovered from <c>TextReasoningContent</c>, which is the only place it lives: <c>AgentResponseUpdate.Text</c>
/// concatenates text-bearing content of the answer and never carried this, which is why reading
/// <c>update.Text</c> alone silently discarded it.
/// </para>
/// <para>
/// Model-dependent by nature: an endpoint that does not emit reasoning produces no such events, and the
/// pipeline does not invent any.
/// </para>
/// </summary>
public sealed class AgentReasoningDelta(string text) : AgentEvent
{
    /// <summary>The fragment.</summary>
    public string Text { get; } = text;
}

/// <summary>A tool is about to run.</summary>
public sealed class AgentToolCallStarted(string toolName) : AgentEvent
{
    /// <summary>Name of the tool, as it reaches the model.</summary>
    public string ToolName { get; } = toolName;
}

/// <summary>
/// A tool finished — successfully, by refusing, or by throwing, which is why the outcome is carried rather
/// than implied.
/// </summary>
public sealed class AgentToolCallCompleted(
    string toolName, string result, AgentToolOutcome outcome, TimeSpan elapsed) : AgentEvent
{
    /// <summary>Name of the tool.</summary>
    public string ToolName { get; } = toolName;

    /// <summary>The tool's result, rendered as text.</summary>
    public string Result { get; } = result;

    /// <summary>How it ended.</summary>
    public AgentToolOutcome Outcome { get; } = outcome;

    /// <summary>Wall-clock duration of the call.</summary>
    public TimeSpan Elapsed { get; } = elapsed;
}

/// <summary>Which entry point produced the run.</summary>
public enum AgentRunKind
{
    /// <summary><c>AIAgent.RunAsync</c> — one response, delivered whole.</summary>
    Complete,

    /// <summary><c>AIAgent.RunStreamingAsync</c> — fragments, delivered as they arrive.</summary>
    Streaming,
}

/// <summary>How a tool call ended.</summary>
public enum AgentToolOutcome
{
    /// <summary>The tool ran and returned.</summary>
    Succeeded,

    /// <summary>A stage refused the call before it ran; <c>Result</c> carries the refusal.</summary>
    Refused,

    /// <summary>The tool threw. The wrapper turns it into an error result, so the run continues.</summary>
    Failed,
}
