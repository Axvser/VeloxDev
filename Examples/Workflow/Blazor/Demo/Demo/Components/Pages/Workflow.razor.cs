using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Specialized;
using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Pages;

public partial class Workflow : ComponentBase, IDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private WorkflowDemoSession? _session;
    private string _agentMessage = "";
    private bool _useStreaming = true;

    private VeloxDev.AI.MCP.McpStatusViewModel? McpStatus
        => (_session?.Tree.GetHelper() as AgentHelper)?.Mcp.Status;
    private string _canvasLayoutSize = "";

    // 运行控制卡片对闸门状态的显示——空闲 / 已暂停 / …
    private string _runGateState = "空闲";

    // 会话是否有可续跑的检查点。缓存而不是每次渲染读取：页面在每个节点/连线变化时重渲染，而 HasCheckpoint 会碰磁盘。
    private bool _hasCheckpoint;

    // ── Link selection ─────────────────────────────────────────────────────
    // 菜单的接线整个在表面组件里（右键入口、定位、开合上报）；页面只读它选中的那条线，画选中态。
    private WorkflowSurfaceBehavior? _surface;

    // ── Agent interaction modals (RequestSelection / RequestConfirmation) ──
    private SelectionRequest? _selection;
    private ConfirmationRequest? _confirmation;

    /// <summary>Active <c>RequestSelection</c> dialog state; rendered by Workflow.razor and
    /// completed by the user's buttons. <see cref="Completion"/> unblocks the Agent tool call.</summary>
    private sealed class SelectionRequest
    {
        public string Prompt = "";
        public string[] Options = [];
        public bool AllowMultiSelect;
        public string FreeTextPrompt = "";
        public string FreeText { get; set; } = "";
        public string? SelectedOption;
        public bool[] Checked = [];
        public TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Active <c>RequestConfirmation</c> dialog state.</summary>
    private sealed class ConfirmationRequest
    {
        public string OperationKey = "";
        public string Description = "";
        public AgentConfirmationResult Result;
        public TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    protected override void OnInitialized()
    {
        _session = WorkflowDemoSession.Create();
        HookLinkKeys(_session.Tree);
        SubscribeSession();
        UpdateCanvasSize();
    }

    private void SubscribeSession()
    {
        if (_session is null) return;
        _session.Tree.Nodes.CollectionChanged += OnNodesOrLinksChanged;
        _session.Tree.Links.CollectionChanged += OnNodesOrLinksChanged;
        _session.Controller.PropertyChanged += OnControllerPropertyChanged;
        // 一轮跑完，检查点这一轮才写得下来 —— 按钮可不可按跟着它走。
        _session.Controller.RunCommand.Exited += OnRunCommandExited;
        _session.Controller.ResumeCommand.Exited += OnRunCommandExited;
        RefreshRunControls();
        if (_session.Tree.Layout is INotifyPropertyChanged lp)
            lp.PropertyChanged += OnLayoutPropertyChanged;
        if (_session.Tree.GetHelper() is AgentHelper helper)
        {
            helper.Mcp.Status.PropertyChanged += OnMcpStatusChanged;
            // Wire the Agent's interaction tools to the page's modal UI. The tool call is
            // marshalled onto the renderer's SynchronizationContext, so these handlers can touch
            // component state directly; the modal blocks the tool until the user answers.
            helper.SelectionHandler = ShowSelectionAsync;
            helper.ConfirmationHandler = ShowConfirmationAsync;
            _ = helper.LoadMcpServersAsync();
        }
        // 跟着会话一起订：OnInitialized / Reset / Load 三处换树都走这里，探针因此总落在屏幕上那棵树。
        VetoFrameworkGestures(_session.Tree);
    }

    private void OnMcpStatusChanged(object? sender, PropertyChangedEventArgs e)
        => _ = InvokeAsync(StateHasChanged);

    private async Task ReloadMcpAsync()
    {
        if (_session?.Tree.GetHelper() is AgentHelper helper)
        {
            await helper.LoadMcpServersAsync();
            await InvokeAsync(StateHasChanged);
        }
    }

    private void UnsubscribeSession()
    {
        if (_session is null) return;
        _session.Tree.Nodes.CollectionChanged -= OnNodesOrLinksChanged;
        _session.Tree.Links.CollectionChanged -= OnNodesOrLinksChanged;
        _session.Controller.PropertyChanged -= OnControllerPropertyChanged;
        _session.Controller.RunCommand.Exited -= OnRunCommandExited;
        _session.Controller.ResumeCommand.Exited -= OnRunCommandExited;
        if (_session.Tree.Layout is INotifyPropertyChanged lp)
            lp.PropertyChanged -= OnLayoutPropertyChanged;
        if (_session.Tree.GetHelper() is AgentHelper helper)
        {
            helper.SelectionHandler = null;
            helper.ConfirmationHandler = null;
            helper.Mcp.Status.PropertyChanged -= OnMcpStatusChanged;
        }
    }

    private void UpdateCanvasSize()
    {
        if (_session?.Tree?.Layout is { } layout)
            _canvasLayoutSize = $"{layout.ActualSize.Width:F0}×{layout.ActualSize.Height:F0}";
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only the canvas size affects the sidebar label. ViewportOffset is written by the surface
        // on every scroll — re-rendering the page for it would re-render the whole tree each frame.
        if (e.PropertyName is nameof(CanvasLayout.ActualSize))
        {
            UpdateCanvasSize();
            InvokeAsync(StateHasChanged);
        }
    }

    // 树/连线的重渲染归表面组件；页面订阅只是为了让侧栏的节点数、连接数跟着变。
    private void OnNodesOrLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private async Task StopWorkflow()
    {
        if (_session is null) return;
        await _session.Controller.CloseWorkflowCommand.ExecuteAsync(null);
    }

    // ── Run controls ───────────────────────────────────────────────────────
    // 门与检查点都在会话上、不在树上，所以这三件事只有拿得到会话时才可按；换过会话就跟着新的走。
    private void RefreshRunControls()
    {
        _hasCheckpoint = _session?.HasCheckpoint == true;
        _runGateState = _session?.Gate.IsPaused == true ? "已暂停" : "空闲";
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnRunCommandExited(CommandEventArgs e) => RefreshRunControls();

    private void PauseWorkflow()
    {
        if (_session is null) return;
        _session.Gate.Pause();
        _runGateState = "已暂停：停在下一个节点边界";
        StateHasChanged();
    }

    private void ResumeWorkflow()
    {
        if (_session is null) return;
        _session.Gate.Resume();
        _runGateState = "运行中";
        StateHasChanged();
    }

    private async Task ContinueFromCheckpoint()
    {
        if (_session is null) return;
        _runGateState = "从检查点继续…";
        StateHasChanged();
        await _session.Controller.ResumeCommand.ExecuteAsync(null);
    }

    private async Task ResetDemo()
    {
        UnsubscribeSession();
        if (_session is not null)
            await _session.Tree.GetHelper().CloseAsync();
        _session = WorkflowDemoSession.Create();
        SubscribeSession();
        UpdateCanvasSize();
        StateHasChanged();
    }

    private async Task Undo()
    {
        if (_session?.Tree?.UndoCommand?.CanExecute(null) == true)
            await _session.Tree.UndoCommand.ExecuteAsync(null);
    }

    private async Task Redo()
    {
        if (_session?.Tree?.RedoCommand?.CanExecute(null) == true)
            await _session.Tree.RedoCommand.ExecuteAsync(null);
    }

    private async Task SaveWorkflow()
    {
        if (_session?.Tree is null) return;
        var json = _session.Tree.Serialize();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes);
        await JS.InvokeVoidAsync("downloadFile", "workflow.json", "application/json", base64);
    }

    private async Task LoadWorkflow()
    {
        try
        {
            var json = await JS.InvokeAsync<string>("openFileDialog", ".json");
            if (string.IsNullOrEmpty(json)) return;

            UnsubscribeSession();
            if (_session is not null)
                await _session.Tree.GetHelper().CloseAsync();

            var tree = json.Deserialize<TreeViewModel>();
            _session = WorkflowDemoSession.FromTree(tree);
        HookLinkKeys(_session.Tree);
            SubscribeSession();
            UpdateCanvasSize();
            // The surface restores the saved viewport position by itself when the new tree reaches it.
            StateHasChanged();
        }
        catch { }
    }

    private async Task SendToAgent()
    {
        if (_session?.Tree is null || string.IsNullOrWhiteSpace(_agentMessage)) return;
        var msg = _agentMessage;
        _agentMessage = "";
        await _session.Tree.AskCommand.ExecuteAsync(msg);
        StateHasChanged();
    }

    private async Task OnAgentKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await SendToAgent();
    }

    private void OnStreamingToggle(ChangeEventArgs e)
    {
        _useStreaming = e.Value?.ToString() == "true";
        if (_session?.Tree is not null)
            _session.Tree.UseStreamingAgentResponse = _useStreaming;
    }

    // ── Agent interaction handlers ─────────────────────────────────────────

    private async Task ShowSelectionAsync(AgentSelectionEventArgs args)
    {
        var req = new SelectionRequest
        {
            Prompt = args.Prompt,
            Options = args.Options.ToArray(),
            AllowMultiSelect = args.AllowMultiSelect,
            FreeTextPrompt = args.FreeTextPrompt,
            Checked = new bool[args.Options.Count],
        };
        _selection = req;
        await InvokeAsync(StateHasChanged);

        // Block until the user answers. The TCS is completed by the modal's buttons below.
        await req.Completion.Task;

        if (req.AllowMultiSelect)
        {
            var selected = new List<string>();
            for (int i = 0; i < req.Options.Length; i++)
                if (req.Checked[i]) selected.Add(req.Options[i]);
            args.SelectedOptions = selected;
        }
        else
        {
            args.SelectedOption = req.SelectedOption;
        }
        args.FreeTextResponse = string.IsNullOrWhiteSpace(req.FreeText) ? null : req.FreeText.Trim();

        _selection = null;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ShowConfirmationAsync(AgentConfirmationEventArgs args)
    {
        var req = new ConfirmationRequest
        {
            OperationKey = args.OperationKey,
            Description = args.Description,
        };
        _confirmation = req;
        await InvokeAsync(StateHasChanged);

        await req.Completion.Task;

        args.Result = req.Result;

        _confirmation = null;
        await InvokeAsync(StateHasChanged);
    }

    private void OnCheckChanged(SelectionRequest sel, int idx, bool value)
        => sel.Checked[idx] = value;

    private void PickOption(string? option)
    {
        if (_selection is not { } sel || sel.AllowMultiSelect) return;
        sel.SelectedOption = option;
        sel.Completion.TrySetResult(true);
    }

    private void ConfirmMultiSelection()
    {
        if (_selection is not { } sel || !sel.AllowMultiSelect) return;
        sel.Completion.TrySetResult(true);
    }

    /// <summary>Answers a single-select request with the typed text, leaving no option chosen.</summary>
    private void SubmitFreeTextSelection()
    {
        if (_selection is not { } sel || sel.AllowMultiSelect) return;
        if (string.IsNullOrWhiteSpace(sel.FreeText)) return;
        sel.SelectedOption = null;
        sel.Completion.TrySetResult(true);
    }

    private void CancelSelection()
    {
        if (_selection is not { } sel) return;
        if (sel.AllowMultiSelect)
            Array.Fill(sel.Checked, false);
        sel.Completion.TrySetResult(true);
    }

    private void CompleteConfirmation(AgentConfirmationResult result)
    {
        if (_confirmation is not { } conf) return;
        conf.Result = result;
        conf.Completion.TrySetResult(true);
    }

    public void Dispose()
    {
        UnsubscribeSession();
    }
    // VeloxDev customization: Delete 归宿主 —— 库只把按键路由过来（target 就是指针停着的那条线），
    // 删不删由这里写（与悬停高亮同一条路）。
    private void HookLinkKeys(IWorkflowTreeViewModel? tree)
    {
        if (tree?.GetHelper() is not IInputEvents events) return;

        events.Input.KeyDown += (_, e) =>
        {
            if (e.Key != InputKey.Delete || e.Handle.PreventDefault) return;
            if (e.Target is not IWorkflowLinkViewModel link || !link.DeleteCommand.CanExecute(null)) return;

            link.DeleteCommand.Execute(null);
        };
    }

    /// <summary>
    /// The gestures this host has claimed: Shift-drag on the empty canvas, and Ctrl-drag anywhere — the
    /// framework's own hand stands down and the host's takes over.
    /// </summary>
    /// <remarks>
    /// Subscribe, test the condition, set <c>PreventDefault</c> — the whole starting point of an interaction of
    /// one's own. The press subscription covers the blank canvas and the cards alike, because the refusal is read
    /// wherever the framework's hand would have started: pan, node drag and slot connection. Re-subscribed on every
    /// tree swap, the same way the session's own wiring is: this component renders many times and the tree is
    /// replaced wholesale by Reset / Load, so a subscription kept on the tree the component first saw would go on
    /// hearing a tree that is no longer on screen.
    /// </remarks>
    private static void VetoFrameworkGestures(TreeViewModel tree)
    {
        var input = ((IInputEvents)tree.GetHelper()).Input;

        input.PointerPressed += (_, e) =>
        {
            if (e.Modifiers.HasFlag(InputModifiers.Control)
                || (e.Target is null && e.Modifiers.HasFlag(InputModifiers.Shift)))
            {
                e.Handle.PreventDefault = true;
            }
        };

        // No wheel subscription here, and that is this adapter's shape rather than an omission: the gesture runs in
        // the browser, whose zoom handler returns before asking .NET unless Ctrl is held — Ctrl+wheel is the only
        // wheel that reaches the verdict, and this demo wants it to zoom. A host that wants Ctrl+wheel for itself
        // instead refuses it on <c>PointerWheelChanged</c>, exactly as the press clause above does.
    }

}
