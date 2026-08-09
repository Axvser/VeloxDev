using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

public partial class WorkflowBoolSelectorView : ComponentBase, IDisposable
{
    [Parameter]
    public BoolSelectorNodeViewModel? Selector { get; set; }

    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    private bool _condition;

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
        _condition = Selector.Condition;
    }

    private void OnConditionChanged(ChangeEventArgs e)
    {
        if (Selector is null) return;
        Selector.Condition = e.Value?.ToString() == "true";
    }

    public void Dispose()
    {
        if (Selector is INotifyPropertyChanged n)
            n.PropertyChanged -= OnSelectorChanged;
    }
}
