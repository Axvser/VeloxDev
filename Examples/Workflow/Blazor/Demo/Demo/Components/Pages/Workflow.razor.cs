using Demo.ViewModels;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Specialized;
using System.ComponentModel;
using VeloxDev.AI;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

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

    private void SubscribeSession()
    {
        if (_session is null) return;
        _session.Tree.Nodes.CollectionChanged += OnNodesOrLinksChanged;
        _session.Tree.Links.CollectionChanged += OnNodesOrLinksChanged;
        _session.Controller.PropertyChanged += OnControllerPropertyChanged;
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
            StateHasChanged();

            // Restore the saved viewing position (mirrors the XAML demos, which read
            // Layout.ViewportOffset after loading and scroll to it).
            var vp = _session.Tree.Layout?.ViewportOffset;
            if (vp is { } viewport && (viewport.Horizontal > 0 || viewport.Vertical > 0))
            {
                await Task.Delay(120); // let the surface lay out the canvas first
                await JS.InvokeVoidAsync("veloxdevWorkflow.scrollToPosition", "wf-scroll", viewport.Horizontal, viewport.Vertical);
            }
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
}
