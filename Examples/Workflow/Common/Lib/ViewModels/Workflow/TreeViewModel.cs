using Demo.ViewModels.Workflow.Helper;
using System.Collections.ObjectModel;
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
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型

    [VeloxProperty] private ObservableCollection<string> executionLog = [];
    [VeloxProperty] private ObservableCollection<string> agentLog = [];
    [VeloxProperty] private ObservableCollection<AgentMessageViewModel> agentMessages = [];
    [VeloxProperty] private string conversationMarkdown = "";
    [VeloxProperty] private bool isWorkflowRunning = false;

    [VeloxProperty] private bool useStreamingAgentResponse = true;

    /// <summary>
    /// 非编译器路径的全局单调执行序号。单独启动某个节点（节点卡片 Run → ReceiveCommand）不再
    /// 每次从 01 复位——每次独立启动都在同一画布上继续递增，徽标与执行日志保持有序。
    /// 编译器路径不受影响（它用 CompileContext.Order 固定编号）。
    /// </summary>
    private long _executionSequence;

    /// <summary>取下一个全局执行序号（非编译器路径）。</summary>
    public int NextExecutionSequence() => (int)Interlocked.Increment(ref _executionSequence);

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
            AppendAgentLog($"🧑 {message}");

            if (UseStreamingAgentResponse)
            {
                await AskStreamingCoreAsync(helper, message);
                return;
            }

            var response = await helper.Agent.RunAsync(
                message, helper.Session, helper.BuildRunOptions());

            if (response is not null)
            {
                var text = response.Text;
                AppendAgentLog($"🤖 {text ?? string.Empty}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendAgentLog($"❌ Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Isolated into a separate method so that the JIT compilation of
    /// <c>IAsyncEnumerable&lt;T&gt;</c> / <c>await foreach</c> does not
    /// prevent <see cref="AskAsync"/> from executing at all when the
    /// required runtime type cannot be resolved.
    /// </summary>
    private static readonly char[] s_sentenceBreaks = ['\n', '\r', '。', '！', '？', '.', '!', '?', '；', ';'];

    private async Task AskStreamingCoreAsync(AgentHelper helper, string message)
    {
        var buffer = new System.Text.StringBuilder();
        var isFirstLine = true;

        await foreach (var response in helper.Agent!.RunStreamingAsync(
            message, helper.Session!, helper.BuildRunOptions()))
        {
            var text = response.Text;
            if (string.IsNullOrEmpty(text))
                continue;

            foreach (var ch in text)
            {
                buffer.Append(ch);

                if (Array.IndexOf(s_sentenceBreaks, ch) >= 0)
                {
                    FlushStreamingBuffer(buffer, ref isFirstLine);
                }
            }
        }

        // flush remaining text
        if (buffer.Length > 0)
        {
            FlushStreamingBuffer(buffer, ref isFirstLine);
        }
    }

    private void FlushStreamingBuffer(System.Text.StringBuilder buffer, ref bool isFirstLine)
    {
        var line = buffer.ToString().TrimEnd('\r', '\n');
        buffer.Clear();

        if (string.IsNullOrWhiteSpace(line))
            return;

        if (isFirstLine)
        {
            AgentLog.Add($"🤖 {line}");
            AgentMessages.Add(new AgentMessageViewModel(AgentMessageRole.Assistant, line));
            isFirstLine = false;
        }
        else
        {
            AgentLog.Add($"    {line}");

            // 把流式的后续片段追加到当前助手消息，使 Markdown（代码块/列表等）能跨行完整渲染。
            if (AgentMessages.Count > 0 &&
                AgentMessages[AgentMessages.Count - 1].Role == AgentMessageRole.Assistant)
            {
                AgentMessages[AgentMessages.Count - 1].Text += $"\n{line}";
            }
        }
    }

    public void BeginWorkflowRun()
    {
        ResetExecutionLog();
        SetWorkflowRunning(true);
    }

    public void EndWorkflowRun()
    {
        SetWorkflowRunning(false);
    }

    public void RefreshWorkflowRunningState()
    {
        var isRunning = Nodes.OfType<NodeViewModel>().Any(node => node.IsRunning || node.RunCount > 0 || node.WaitCount > 0);
        SetWorkflowRunning(isRunning);
    }

    public void ResetExecutionLog()
    {
        ExecutionLog.Clear();
        _executionSequence = 0;

        foreach (var node in Nodes.OfType<NodeViewModel>())
        {
            node.LastExecutionOrder = 0;
            node.LastExecutionTrace = "未执行";
            node.LastStatus = "Idle";
            node.LastDuration = "-";
            node.LastError = string.Empty;
            node.IsRunning = false;
            node.RunCount = 0;
            node.WaitCount = 0;
        }

        SetWorkflowRunning(false);
    }

    public void AppendExecutionLog(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }
        ExecutionLog.Add(entry);
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

    // ── 会话 Markdown 转录（供 Avalonia Full Demo 直接喂给 AvalonMarkdown MarkdownView） ──

    partial void OnItemAddedToAgentMessages(IEnumerable<AgentMessageViewModel> items)
    {
        foreach (var msg in items)
            msg.PropertyChanged += OnAgentMessageTextChanged;
        RebuildConversationMarkdown();
    }

    partial void OnItemRemovedFromAgentMessages(IEnumerable<AgentMessageViewModel> items)
    {
        foreach (var msg in items)
            msg.PropertyChanged -= OnAgentMessageTextChanged;
        RebuildConversationMarkdown();
    }

    partial void OnItemMovedInAgentMessages(IEnumerable<AgentMessageViewModel> items)
    {
        RebuildConversationMarkdown();
    }

    partial void OnItemsResetInAgentMessages()
    {
        RebuildConversationMarkdown();
    }

    private void OnAgentMessageTextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentMessageViewModel.Text))
            RebuildConversationMarkdown();
    }

    private void RebuildConversationMarkdown()
    {
        var sb = new StringBuilder();

        foreach (var msg in AgentMessages)
        {
            if (sb.Length > 0)
                sb.Append("\n\n---\n\n");

            switch (msg.Role)
            {
                case AgentMessageRole.User:
                    sb.Append("**🧑 你：** ").Append(msg.Text);
                    break;
                case AgentMessageRole.Assistant:
                    sb.Append("**🤖 助手：**\n\n").Append(msg.Text);
                    break;
                case AgentMessageRole.Error:
                    sb.Append("**❌ 错误：** ").Append(msg.Text);
                    break;
                default:
                    sb.Append(msg.Text);
                    break;
            }
        }

        ConversationMarkdown = sb.ToString();
    }

    [VeloxCommand]
    private async Task Save(object? parameter)
    {
        if (parameter is not string path) return;
        await Helper.CloseAsync();
        var json = this.Serialize();
        using var writer = new StreamWriter(path, append: false);
        await writer.WriteAsync(json).ConfigureAwait(false);
    }

    [VeloxCommand]
    private Task AgentContextTest()
    {
        var context = this.AsAgentScope()
            .WithPromptLanguage(AgentLanguages.Chinese)
            .WithComponents([
                typeof(NodeViewModel),
                typeof(ControllerViewModel),
                typeof(SlotViewModel),
                typeof(LinkViewModel),
                typeof(TreeViewModel)])
            .ProvideAllContexts(AgentLanguages.English);

        ExecutionLog.Add(context);

        File.WriteAllText(@"E://agent.md", context);

        return Task.CompletedTask;
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
