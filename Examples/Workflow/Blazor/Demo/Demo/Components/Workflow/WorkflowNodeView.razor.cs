using Microsoft.AspNetCore.Components;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// Generic catch-all node body. The demo's node types (Controller/Timer/Enum/Python) each get a
/// dedicated body view; this generic body is only used for node types without a specific view.
/// It no longer depends on the pruned plain-worker/Bool-selector view-models, so it reads only
/// what <see cref="IWorkflowNodeViewModel"/> exposes and lets the surrounding
/// <see cref="TemplateNodeView"/> card render title/execution feedback.
/// </summary>
public partial class WorkflowNodeView : ComponentBase, IDisposable
{
    [Parameter]
    public IWorkflowNodeViewModel? Node { get; set; }

    protected override void OnInitialized()
    {
        if (Node is INotifyPropertyChanged n)
            n.PropertyChanged += OnNodePropertyChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        if (Node is INotifyPropertyChanged n)
            n.PropertyChanged -= OnNodePropertyChanged;
    }
}
