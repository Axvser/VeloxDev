using Demo.ViewModels;
using Demo.Workflow;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private string _canvasLayoutSize = "";
    private readonly List<IWorkflowNodeViewModel> _subscribedNodes = [];
    private INotifyPropertyChanged? _subscribedVirtualLink;

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
        // VirtualLink object, not the tree), so subscribe directly to redraw the gesture.
        if (_session.Tree.VirtualLink is INotifyPropertyChanged vp)
        {
            vp.PropertyChanged += OnVirtualLinkPropertyChanged;
            _subscribedVirtualLink = vp;
        }
        SubscribeNodeChanges();
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
        UnsubscribeNodeChanges();
    }

    private void SubscribeNodeChanges()
    {
        if (_session?.Tree?.Nodes is null) return;
        foreach (var node in _session.Tree.Nodes)
        {
            if (node is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += OnNodePropertyChanged;
                _subscribedNodes.Add(node);
            }
        }
    }

    private void UnsubscribeNodeChanges()
    {
        foreach (var node in _subscribedNodes)
        {
            if (node is INotifyPropertyChanged npc)
                npc.PropertyChanged -= OnNodePropertyChanged;
        }
        _subscribedNodes.Clear();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Live node position + link updates while dragging (Anchor/Size changes).
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
            InvokeAsync(StateHasChanged);
    }

    private void UpdateCanvasSize()
    {
        if (_session?.Tree?.Layout is { } layout)
            _canvasLayoutSize = $"{layout.ActualSize.Width:F0}×{layout.ActualSize.Height:F0}";
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateCanvasSize();
        InvokeAsync(StateHasChanged);
    }

    private void OnNodesOrLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribeNodeChanges();
        SubscribeNodeChanges();
        InvokeAsync(StateHasChanged);
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnVirtualLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

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

    public void Dispose()
    {
        UnsubscribeSession();
    }
}
