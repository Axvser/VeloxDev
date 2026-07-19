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
    private ElementReference _canvasRef;
    private ElementReference _canvasScrollRef;
    private ElementReference _agentLogRef;
    private ElementReference _execLogRef;

    private string _agentMessage = "";
    private bool _useStreaming = true;
    private double _canvasW = 3200;
    private double _canvasH = 2200;
    private string _canvasLayoutSize = "";

    private string _canvasWidth => $"{_canvasW}px";
    private string _canvasHeight => $"{_canvasH}px";

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
    }

    private void UnsubscribeSession()
    {
        if (_session is null) return;
        _session.Tree.Nodes.CollectionChanged -= OnNodesOrLinksChanged;
        _session.Tree.Links.CollectionChanged -= OnNodesOrLinksChanged;
        _session.Controller.PropertyChanged -= OnControllerPropertyChanged;
        if (_session.Tree is INotifyPropertyChanged np)
            np.PropertyChanged -= OnTreePropertyChanged;
    }

    private void UpdateCanvasSize()
    {
        if (_session?.Tree?.Layout is { } layout)
        {
            _canvasW = Math.Max(layout.ActualSize.Width, 1600);
            _canvasH = Math.Max(layout.ActualSize.Height, 1200);
            _canvasLayoutSize = $"{layout.ActualSize.Width:F0}×{layout.ActualSize.Height:F0}";
        }
    }

    private void OnNodesOrLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => InvokeAsync(StateHasChanged);

    private async Task RunWorkflow()
    {
        if (_session is null) return;
        await _session.Controller.OpenWorkflowCommand.ExecuteAsync(null);
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
        }
        catch
        {
            // User cancelled or error
        }
    }

    private async Task LoadPerformanceTest()
    {
        UnsubscribeSession();
        if (_session is not null)
            await _session.Tree.GetHelper().CloseAsync();

        var perfSession = PerformanceTestSession.Create();
        _session = WorkflowDemoSession.FromTree(perfSession.Tree);
        SubscribeSession();
        UpdateCanvasSize();
        StateHasChanged();
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

    private string GetNodeStyle(IWorkflowNodeViewModel node)
        => $"left:{node.Anchor.Horizontal:F0}px; top:{node.Anchor.Vertical:F0}px; width:{node.Size.Width:F0}px; z-index:{node.Anchor.Layer};";

    private string BuildLinkPoints(IWorkflowLinkViewModel link)
    {
        var from = link.Sender?.Parent;
        var to = link.Receiver?.Parent;
        if (from is null || to is null) return "";

        double sx = from.Anchor.Horizontal + from.Size.Width;
        double sy = from.Anchor.Vertical + from.Size.Height / 2;
        double ex = to.Anchor.Horizontal;
        double ey = to.Anchor.Vertical + to.Size.Height / 2;

        double dx = ex - sx;
        const double phi = 0.6180339887;
        double stub = Math.Abs(dx) / 2.0 * (1.0 - phi);
        double p1x = sx + stub;
        double p4x = ex - stub;

        return $"{sx:F1},{sy:F1} {p1x:F1},{sy:F1} {p4x:F1},{ey:F1} {ex:F1},{ey:F1}";
    }

    public void Dispose()
    {
        UnsubscribeSession();
    }
}
