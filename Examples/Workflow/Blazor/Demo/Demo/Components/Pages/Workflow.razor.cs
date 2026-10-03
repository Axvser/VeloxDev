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
using VeloxDev.MVVM.Serialization;
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
    private INotifyPropertyChanged? _subscribedVirtualLink;

    /// <summary>What the run-controls card says about the gate — 空闲 / 已暂停 / …</summary>
    private string _runGateState = "空闲";

    /// <summary>Whether the session has a checkpoint to carry on from. Cached rather than read per
    /// render: the page re-renders on every node/link change, and <c>HasCheckpoint</c> hits the disk.</summary>
    private bool _hasCheckpoint;

    // ── Link selection / context menu ──────────────────────────────────────
    // 悬停/选中归 Core 的交互枢纽（见 BindInteraction）；页面只留右键菜单这一份浏览器侧状态
    private IWorkflowLinkViewModel? _menuLink;
    private int _menuLeft;
    private int _menuTop;
    private ElementReference _linksLayer;

    // 当前树那一个交互枢纽（Core 按树缓存）。换过树就换实例，所以订阅按实例比对重新接
    private LinkInteraction? _subscribedInteraction;

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
        SubscribeSession();
        UpdateCanvasSize();
    }

    // 枢纽按树取用；换树换成另一个实例，所以订阅按实例比对重新接
    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        BindInteraction();
        return base.OnAfterRenderAsync(firstRender);
    }

    private void BindInteraction()
    {
        var interaction = _session?.Tree is { } tree ? LinkInteraction.For(tree) : null;
        if (ReferenceEquals(interaction, _subscribedInteraction)) return;

        if (_subscribedInteraction is not null)
        {
            _subscribedInteraction.HoverChanged -= OnHubHoverChanged;
            _subscribedInteraction.LinkPressed -= OnHubLinkPressed;
        }

        _subscribedInteraction = interaction;
        if (interaction is not null)
        {
            interaction.HoverChanged += OnHubHoverChanged;
            interaction.LinkPressed += OnHubLinkPressed;
        }
    }

    // 悬停后把键盘焦点收进连线层 —— Delete 只在这层有焦点时才到得了页面。
    // 不触发重渲染：高亮是每条线自己的本地悬停态，枢纽只负责给 Delete 一个答案
    private void OnHubHoverChanged(object? sender, LinkHoverEventArgs e)
    {
        if (e.Link is not null)
        {
            _ = _linksLayer.FocusAsync(preventScroll: true);
        }
    }

    // 右键菜单由 hub 的 LinkPressed 驱动：只有命中连线才会发，落在空白处不会有事件
    private void OnHubLinkPressed(object? sender, LinkPressedEventArgs e)
    {
        if (e.Button != PointerButtonKind.Right) return;
        _menuLink = e.Link;
        // 菜单一开指针就落到菜单上，那之后的进出都不该取消菜单针对的这条线
        if (_subscribedInteraction is not null) _subscribedInteraction.IsSuspended = true;
        StateHasChanged();
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
        if (_session.Tree is INotifyPropertyChanged np)
            np.PropertyChanged += OnTreePropertyChanged;
        if (_session.Tree.Layout is INotifyPropertyChanged lp)
            lp.PropertyChanged += OnLayoutPropertyChanged;
        // The VirtualLink raises its own PropertyChanged (Send/Receive/Reset only mutate the
        // VirtualLink object, not the tree), so subscribe directly to add/remove the gesture view.
        if (_session.Tree.VirtualLink is INotifyPropertyChanged vp)
        {
            vp.PropertyChanged += OnVirtualLinkPropertyChanged;
            _subscribedVirtualLink = vp;
        }
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
        if (_session.Tree is INotifyPropertyChanged np)
            np.PropertyChanged -= OnTreePropertyChanged;
        if (_session.Tree.Layout is INotifyPropertyChanged lp)
            lp.PropertyChanged -= OnLayoutPropertyChanged;
        if (_subscribedVirtualLink is not null)
        {
            _subscribedVirtualLink.PropertyChanged -= OnVirtualLinkPropertyChanged;
            _subscribedVirtualLink = null;
        }
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

    private void OnNodesOrLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnVirtualLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The per-move coordinate changes are handled by the VirtualLink's own TemplateLinkView
        // subscription; the page only needs to add/remove the gesture view when IsVisible flips.
        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible) or null or "")
        {
            InvokeAsync(StateHasChanged);
        }
    }

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

    // ── Link selection handlers ────────────────────────────────────────────

    // 菜单位置只有浏览器事件知道：这里只记下按下点，开菜单交给 hub 的 LinkPressed
    private void OnLinkContextMenu(MouseEventArgs e)
    {
        // 客户端坐标取整后写出去：整数字符串没有小数点，区域设置就碰不到它
        _menuLeft = (int)Math.Round(e.ClientX);
        _menuTop = (int)Math.Round(e.ClientY);
    }

    private void CloseLinkMenu()
    {
        if (_menuLink is null) return;
        if (_subscribedInteraction is not null) _subscribedInteraction.IsSuspended = false;
        _menuLink = null;
        StateHasChanged();
    }

    private void DeleteLinkFromMenu() => DeleteLink(_menuLink);

    private void OnLinksKeyDown(KeyboardEventArgs e)
    {
        // Delete 不在这里转发：表面的根元素自己绑了 keydown（那是适配器给的默认按键路由，生成出来的
        // 工程零代码就有），而这个链接层就在表面之内 —— 两边都转发时，一次按键会删两遍，第二遍打在
        // 一条已经不在树上的连线上。这里只留菜单那条策略。
        if (e.Key == "Escape")
        {
            CloseLinkMenu();
        }
    }

    private void DeleteLink(IWorkflowLinkViewModel? link)
    {
        _menuLink = null;

        // 与另外六家一致：不看 CanExecute。命令自己会排队或拒绝，调用方替它做判断只会让两边不一致
        link?.DeleteCommand.Execute(null);
        StateHasChanged();
    }

    public void Dispose()
    {
        if (_subscribedInteraction is not null)
        {
            _subscribedInteraction.HoverChanged -= OnHubHoverChanged;
            _subscribedInteraction.LinkPressed -= OnHubLinkPressed;
            _subscribedInteraction = null;
        }

        UnsubscribeSession();
    }
}
