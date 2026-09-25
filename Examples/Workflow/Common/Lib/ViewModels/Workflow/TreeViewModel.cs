using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI.Pipelines;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
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

    public void BeginWorkflowRun()
    {
        ResetExecutionLog();
        SetWorkflowRunning(true);
    }

    public void RefreshWorkflowRunningState()
    {
        var isRunning = Nodes.OfType<ControllerViewModel>().Any(c => c.IsActive);
        SetWorkflowRunning(isRunning);
    }

    public void ResetExecutionLog()
    {
        ExecutionLog.Clear();
        SetWorkflowRunning(false);
    }

    public void AppendAgentLog(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }
        AgentLog.Add(entry);
        AgentMessages.Add(AgentMessageViewModel.FromLogLine(entry));
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

    partial void OnItemAddedToAgentMessages(IEnumerable<AgentMessageViewModel> items)
    {
        foreach (var msg in items)
            msg.PropertyChanged += OnAgentMessageTextChanged;
    }

    partial void OnItemRemovedFromAgentMessages(IEnumerable<AgentMessageViewModel> items)
    {
        foreach (var msg in items)
            msg.PropertyChanged -= OnAgentMessageTextChanged;
    }

    partial void OnItemMovedInAgentMessages(IEnumerable<AgentMessageViewModel> items)
    {
    }

    partial void OnItemsResetInAgentMessages()
    {
    }

    private void OnAgentMessageTextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Nothing to do: ConversationMarkdown is rendered from the transcript, and AgentMessages survives
        // only as the plain-text log's companion.
    }

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
        var lines = _subscribedTranscript.ToPlainTextLines();
        if (AgentLog.Count == lines.Count && AgentLog.SequenceEqual(lines)) return;

        AgentLog.Clear();
        foreach (var line in lines) AgentLog.Add(line);
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
        // itself only changes when a new entry opens.
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
}
