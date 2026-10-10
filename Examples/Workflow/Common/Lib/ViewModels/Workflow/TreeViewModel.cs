using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI.Pipelines;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Tree组件之一")]
[AgentContext(AgentLanguages.English, "The workflow tree (canvas). Contains all nodes, slots, and connections. This is the root scope the Agent operates on.")]
[WorkflowBuilder.Tree<AgentHelper>]
public partial class TreeViewModel
{
    public TreeViewModel()
    {
        InitializeWorkflow();
        SubscribeTranscript();
    }

    // …… freely extend your workflow tree view-model

    [VeloxProperty] private ObservableCollection<string> executionLog = [];
    [VeloxProperty] private ObservableCollection<string> agentLog = [];
    [VeloxProperty] private ObservableCollection<AgentMessageViewModel> agentMessages = [];
    [VeloxProperty] private string conversationMarkdown = "";
    [VeloxProperty] private bool isWorkflowRunning = false;

    [VeloxProperty] private bool useStreamingAgentResponse = true;

    /// <summary>Sends a user message to the Agent. The pipeline reports the run, so this host records nothing.</summary>
    [VeloxCommand]
    public async Task AskAsync(object? parameter, CancellationToken ct)
    {
        if (parameter is not string message ||
            Helper is not AgentHelper helper ||
            helper.Agent is null ||
            helper.Session is null)
            return;

        try
        {
            // Nothing is recorded here any more. The pipeline reports the run — the prompt, the answer,
            // the reasoning and every tool call — and the panel renders from that, so this host neither
            // parses the stream nor keeps a second copy of the conversation.
            if (UseStreamingAgentResponse)
            {
                await AskStreamingCoreAsync(helper, message);
                return;
            }

            // No run options: the agent's context providers supply the tool set on every invocation.
            await helper.Agent.RunAsync(message, helper.Session);
        }
        catch (OperationCanceledException)
        {
            // The pipeline reports cancellation as its own outcome; a cancelled run is not an error to
            // render, and it is not this host's job to decide that any more.
        }
        catch (Exception)
        {
            // Likewise: the wrapper turns a fault into AgentTurnFaulted, which the pipeline renders.
        }
    }

    /// <summary>
    /// Drives one streaming run to completion.
    /// <para>
    /// Isolated into a separate method so that the JIT compilation of
    /// <c>IAsyncEnumerable&lt;T&gt;</c> / <c>await foreach</c> does not prevent <see cref="AskAsync"/> from
    /// executing at all when the required runtime type cannot be resolved.
    /// </para>
    /// <para>
    /// The body is a drain: reading the stream is what makes the run happen, and the pipeline — attached as
    /// agent middleware — sees every update on its way past. This used to buffer characters, split them on
    /// punctuation and push the pieces into two collections, which is where the answer-after-a-tool-call
    /// was lost and where a long reply cost a full re-render per fragment.
    /// </para>
    /// </summary>
    private static async Task AskStreamingCoreAsync(AgentHelper helper, string message)
    {
        await foreach (var _ in helper.Agent!.RunStreamingAsync(message, helper.Session!))
        {
        }
    }

    /// <summary>Marks the workflow running and clears the execution log; the UI calls this when a run starts.</summary>
    public void BeginWorkflowRun()
    {
        ResetExecutionLog();
        SetWorkflowRunning(true);
    }

    /// <summary>Recomputes the running flag from the controllers' active state — needed after the tree is edited.</summary>
    public void RefreshWorkflowRunningState()
    {
        var isRunning = Nodes.OfType<ControllerViewModel>().Any(c => c.IsActive);
        SetWorkflowRunning(isRunning);
    }

    /// <summary>Clears the execution log and the running flag.</summary>
    public void ResetExecutionLog()
    {
        ExecutionLog.Clear();
        SetWorkflowRunning(false);
    }

    /// <summary>
    /// Adds one parsed message to <see cref="AgentMessages"/>, the structured companion list.
    /// </summary>
    /// <remarks>
    /// It deliberately does not touch <see cref="AgentLog"/>: that log is a projection of the transcript
    /// (see <see cref="RebuildConversationMarkdown"/>), and a line written here directly would be erased by
    /// the next render — the transcript does not know about it. Anything that belongs in the conversation
    /// goes through the transcript; <see cref="AppendAgentError"/> is that path for a failure.
    /// </remarks>
    public void AppendAgentLog(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }
        AgentMessages.Add(AgentMessageViewModel.FromLogLine(entry));
    }

    /// <summary>
    /// Records a failure in the conversation, the way a turn's own failures arrive.
    /// </summary>
    /// <remarks>
    /// Through the transcript rather than into <see cref="AgentLog"/>, so it reaches every host — the
    /// markdown panel included — and survives: each renderer writes its own prefix (<c>[Error] …</c> for a
    /// line, <c>**错误：**</c> for markdown), so pass the message bare.
    /// </remarks>
    public void AppendAgentError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        (Helper as AgentHelper)?.Transcript.AddError(message);
    }

    /// <summary>
    /// Records a tool call as its own message rather than a log line. The panels render these collapsed —
    /// a tool result is a JSON payload, and printing one inline buries the conversation it interrupted.
    /// </summary>
    public void AppendToolCall(string toolName, string result)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return;
        AgentMessages.Add(AgentMessageViewModel.ToolCall(toolName, result));
    }

    // ── Session Markdown transcript (fed directly to the AvalonMarkdown MarkdownView in the Avalonia Full Demo) ──

    /// <summary>
    /// Renders the conversation from the pipeline's transcript, which owns the message boundaries and the
    /// tool-call grouping. This host used to derive both from its own message list, which is where the
    /// answer-after-a-tool-call was lost and where a tool call became a separator with no text.
    /// </summary>
    private void RebuildConversationMarkdown()
    {
        if (_subscribedTranscript is null)
        {
            ConversationMarkdown = string.Empty;
            AgentLog.Clear();
            return;
        }

        ConversationMarkdown = _subscribedTranscript.ToMarkdown();

        // The platforms that bind the plain-text log get the same conversation, tool calls included —
        // they used to miss those entirely, because a tool call only ever reached the message list.
        //
        // Synced from the tail, not by Clear + re-add: the transcript only ever appends an entry and only
        // ever grows the last one, so everything before the change already matches. Replacing the list whole
        // fires one collection notification per line, and every bound list re-lays out once per notification
        // — with a few hundred entries that is the bulk of what a turn costs this host.
        var lines = _subscribedTranscript.ToPlainTextLines();

        var common = 0;
        var shared = Math.Min(AgentLog.Count, lines.Count);
        while (common < shared && AgentLog[common] == lines[common]) common++;

        while (AgentLog.Count > common) AgentLog.RemoveAt(AgentLog.Count - 1);
        for (var i = common; i < lines.Count; i++) AgentLog.Add(lines[i]);
    }

    private readonly SynchronizationContext? _renderContext = SynchronizationContext.Current;
    private AgentTranscript? _subscribedTranscript;

    /// <summary>
    /// Follows the helper's transcript, rebuilding the panel whenever an entry is added or grows. Called
    /// whenever the tree is (re)subscribed, since loading a workflow replaces the helper.
    /// </summary>
    private void SubscribeTranscript()
    {
        var transcript = (Helper as AgentHelper)?.Transcript;
        if (ReferenceEquals(transcript, _subscribedTranscript))
        {
            RebuildConversationMarkdown();
            return;
        }

        if (_subscribedTranscript is not null)
        {
            _subscribedTranscript.Entries.CollectionChanged -= OnTranscriptEntriesChanged;
            foreach (var entry in _subscribedTranscript.Entries)
                entry.PropertyChanged -= OnTranscriptEntryChanged;
        }

        _subscribedTranscript = transcript;

        if (_subscribedTranscript is not null)
        {
            _subscribedTranscript.Entries.CollectionChanged += OnTranscriptEntriesChanged;
            foreach (var entry in _subscribedTranscript.Entries)
                entry.PropertyChanged += OnTranscriptEntryChanged;
        }

        RebuildConversationMarkdown();
    }

    private void OnTranscriptEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (AgentTranscriptEntry entry in e.OldItems) entry.PropertyChanged -= OnTranscriptEntryChanged;
        if (e.NewItems is not null)
            foreach (AgentTranscriptEntry entry in e.NewItems) entry.PropertyChanged += OnTranscriptEntryChanged;

        // A streamed entry grows in place, so its text change is what moves the panel — the collection
        // itself only changes when a new entry opens, and that renders at once: a new bubble appearing a
        // throttle late reads as a stall. What the throttle bounds is the growth above.
        RebuildConversationMarkdown();
    }

    private void OnTranscriptEntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentTranscriptEntry.Text))
            QueueConversationRender();
    }

    private readonly object _renderGate = new();
    private Timer? _renderTimer;
    private bool _renderDirty;

    /// <summary>How long the panel may lag behind the stream.</summary>
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Throttles the render to <see cref="RenderInterval"/>.
    /// <para>
    /// A flag alone does not work here, which is worth writing down because it looks like it should: every
    /// fragment is published through a post to the UI thread and awaited, so each one gets its own
    /// dispatcher turn. A flag set and cleared within a turn coalesces nothing — the fragments are already
    /// serialized by the dispatcher, and each still costs a full rebuild of the document.
    /// </para>
    /// <para>
    /// What actually bounds the cost is a clock: fragments accumulate, and at most one render runs per
    /// interval no matter how fast they arrive. The panel only ever shows the latest text, so the
    /// intermediate ones are work nobody asked for.
    /// </para>
    /// </summary>
    private void QueueConversationRender()
    {
        lock (_renderGate)
        {
            _renderDirty = true;
            if (_renderTimer is not null) return;

            _renderTimer = new Timer(_ =>
            {
                lock (_renderGate)
                {
                    if (!_renderDirty)
                    {
                        _renderTimer?.Dispose();
                        _renderTimer = null;
                        return;
                    }
                    _renderDirty = false;
                }

                var ui = _renderContext;
                if (ui is null || ReferenceEquals(ui, SynchronizationContext.Current)) RenderConversation();
                else ui.Post(_ => RenderConversation(), null);
            }, null, RenderInterval, RenderInterval);
        }
    }

    /// <summary>Renders now, wherever the caller is.</summary>
    private void RenderConversation() => RebuildConversationMarkdown();

    [VeloxCommand]
    private async Task Save(object? parameter)
    {
        if (parameter is not string path) return;
        await Helper.CloseAsync();
        var json = this.Serialize();
        using var writer = new StreamWriter(path, append: false);
        await writer.WriteAsync(json).ConfigureAwait(false);
    }

    private void SetWorkflowRunning(bool isRunning)
    {
        if (IsWorkflowRunning != isRunning)
        {
            IsWorkflowRunning = isRunning;
        }

        if (Nodes.OfType<ControllerViewModel>().FirstOrDefault() is { } controller && controller.IsActive != isRunning)
        {
            controller.IsActive = isRunning;
        }
    }

    // ── 编译结构（侧栏列表）──────────────────────────────────────────────────

    /// <summary>
    /// The compiled structure of the tree's controller, flattened for a list view — one row per segment, indented by
    /// <see cref="CompiledOutlineRow.Depth"/>. Empty until something compiles.
    /// </summary>
    /// <remarks>
    /// Owned by the tree rather than read through <c>Nodes[...]</c> so a side panel can bind it with a plain path.
    /// The controller pushes into it (see <see cref="RefreshCompiledStructure"/>) the same way it already pushes
    /// <c>BeginWorkflowRun</c>/<c>RefreshWorkflowRunningState</c> — this view model has no subscription to a
    /// controller's property changes, and adding one just for this would be a second mechanism for the same news.
    /// </remarks>
    public ObservableCollection<CompiledOutlineRow> CompiledStructure { get; } = [];

    /// <summary>Rebuilds <see cref="CompiledStructure"/> from <paramref name="controller"/>'s compiled graphs.</summary>
    /// <param name="controller">The controller that just compiled.</param>
    internal void RefreshCompiledStructure(ControllerViewModel controller)
    {
        CompiledStructure.Clear();
        if (controller.Compiler.Graphs.FirstOrDefault() is not { } graph) return;

        foreach (var row in CompiledOutline.Of(graph)) CompiledStructure.Add(row);
    }
}
