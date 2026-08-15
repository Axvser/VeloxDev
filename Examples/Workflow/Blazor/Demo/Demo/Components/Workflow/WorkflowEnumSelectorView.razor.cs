using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

public partial class WorkflowEnumSelectorView : ComponentBase, IDisposable
{
    [Parameter]
    public EnumSelectorNodeViewModel? Selector { get; set; }

    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    private object? _selectedValue;
    private object[] _items = [];
    private RouterCompileMode _compileMode = RouterCompileMode.Dynamic;
    private readonly RouterCompileMode[] _modeOptions = [RouterCompileMode.Static, RouterCompileMode.Dynamic];

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
        _selectedValue = Selector.SelectedValue;
        _items = Selector.EnumValues ?? [];
        _compileMode = Selector.CompileMode;
    }

    private void OnValueChanged(ChangeEventArgs e)
    {
        if (Selector is null) return;
        Selector.SelectedValue = e.Value;
    }

    private void OnModeChanged(ChangeEventArgs e)
    {
        if (Selector is null || e.Value is null) return;
        if (Enum.TryParse<RouterCompileMode>(e.Value.ToString(), out var mode))
            Selector.CompileMode = mode;
    }

    public void Dispose()
    {
        if (Selector is INotifyPropertyChanged n)
            n.PropertyChanged -= OnSelectorChanged;
    }
}
