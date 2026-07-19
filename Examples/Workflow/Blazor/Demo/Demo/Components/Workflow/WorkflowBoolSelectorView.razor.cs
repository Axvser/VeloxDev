using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace Demo.Components.Workflow;

public partial class WorkflowBoolSelectorView : ComponentBase, IDisposable
{
    [Parameter]
    public BoolSelectorNodeViewModel? Selector { get; set; }

    private string _title = "";
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
        _title = Selector.Title;
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
