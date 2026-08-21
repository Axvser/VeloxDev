using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

public partial class WorkflowPythonNodeView : ComponentBase, IDisposable
{
    [Parameter]
    public PythonScriptNodeViewModel? Selector { get; set; }

    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    private string _script = "";

    protected override void OnInitialized()
    {
        SyncFromViewModel();
        if (Selector is INotifyPropertyChanged n)
            n.PropertyChanged += OnSelectorChanged;
    }

    private void OnSelectorChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(() => { SyncFromViewModel(); StateHasChanged(); });
    }

    private void SyncFromViewModel()
    {
        if (Selector is null) return;
        _script = Selector.Script;
    }

    private void OnScriptChanged(ChangeEventArgs e)
    {
        if (Selector is null) return;
        _script = e.Value?.ToString() ?? "";
        Selector.Script = _script;
    }

    public void Dispose()
    {
        if (Selector is INotifyPropertyChanged n)
            n.PropertyChanged -= OnSelectorChanged;
    }
}
