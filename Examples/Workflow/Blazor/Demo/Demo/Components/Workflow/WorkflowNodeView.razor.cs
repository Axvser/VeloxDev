using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace Demo.Components.Workflow;

public partial class WorkflowNodeView : ComponentBase, IDisposable
{
    [Parameter]
    public NodeViewModel? Node { get; set; }

    private string _title = "";
    private string _duration = "";
    private string _orderText = "";
    private string _loadText = "";
    private int _delayMs;
    private int _priority;
    private bool _autoBroadcast;
    private bool _hasOrderBadge;
    private bool _hasLoadBadge;

    private bool HasOrderBadge => _hasOrderBadge;
    private bool HasLoadBadge => _hasLoadBadge;

    protected override void OnInitialized()
    {
        SyncFromViewModel();
        if (Node is INotifyPropertyChanged n)
            n.PropertyChanged += OnNodePropertyChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(() =>
        {
            SyncFromViewModel();
            StateHasChanged();
        });
    }

    private void SyncFromViewModel()
    {
        if (Node is null) return;
        _title = Node.Title;
        _duration = Node.LastDuration;
        _orderText = Node.ExecutionOrderText;
        _loadText = Node.WorkLoadText;
        _delayMs = Node.DelayMilliseconds;
        _priority = Node.CompilePriority;
        _autoBroadcast = Node.AutoBroadcast;
        _hasOrderBadge = Node.HasExecutionOrder;
        _hasLoadBadge = Node.HasWorkLoad;
    }

    private string GetBorderStyle() => Node?.ChromeBorderBrush is { } b
        ? $"border-color:{b};" : "border-color:#4B5563;";

    private string GetHeaderStyle() => Node?.HeaderBackground is { } h
        ? $"background:{h};" : "background:#2d2d2d;";

    private string GetDurationStyle() => Node?.DurationForeground is { } f
        ? $"color:{f};" : "color:#7EC8FF;";

    private void OnDelayChanged(ChangeEventArgs e)
    {
        if (Node is null) return;
        if (int.TryParse(e.Value?.ToString(), out var ms))
            Node.DelayMilliseconds = ms;
    }

    private void OnPriorityChanged(ChangeEventArgs e)
    {
        if (Node is null) return;
        if (int.TryParse(e.Value?.ToString(), out var p))
            Node.CompilePriority = p;
    }

    private void OnTitleChanged(ChangeEventArgs e)
    {
        if (Node is null) return;
        Node.Title = e.Value?.ToString() ?? "";
    }

    private void OnAutoBroadcastChanged(ChangeEventArgs e)
    {
        if (Node is null) return;
        Node.AutoBroadcast = e.Value?.ToString() == "true";
    }

    public void Dispose()
    {
        if (Node is INotifyPropertyChanged n)
            n.PropertyChanged -= OnNodePropertyChanged;
    }
}
