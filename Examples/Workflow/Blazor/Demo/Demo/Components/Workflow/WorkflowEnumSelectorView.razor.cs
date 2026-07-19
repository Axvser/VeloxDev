using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace Demo.Components.Workflow;

public partial class WorkflowEnumSelectorView : ComponentBase, IDisposable
{
    [Parameter]
    public EnumSelectorNodeViewModel? Selector { get; set; }

    private string _title = "";
    private object? _selectedValue;
    private object[] _items = [];

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
        _selectedValue = Selector.SelectedValue;
        _items = Selector.EnumValues ?? [];
    }

    private void OnValueChanged(ChangeEventArgs e)
    {
        if (Selector is null) return;
        Selector.SelectedValue = e.Value;
    }

    public void Dispose()
    {
        if (Selector is INotifyPropertyChanged n)
            n.PropertyChanged -= OnSelectorChanged;
    }
}
