using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace Demo.Components.Workflow;

public partial class WorkflowControllerView : ComponentBase, IDisposable
{
    [Parameter]
    public ControllerViewModel? Controller { get; set; }

    private string _seedValue = "";

    protected override void OnInitialized()
    {
        SyncFromViewModel();
        if (Controller is INotifyPropertyChanged n)
            n.PropertyChanged += OnControllerPropertyChanged;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(() =>
        {
            SyncFromViewModel();
            StateHasChanged();
        });
    }

    private void SyncFromViewModel()
    {
        if (Controller is null) return;
        _seedValue = Controller.SeedPayload;
    }

    private void OnSeedChanged(ChangeEventArgs e)
    {
        if (Controller is null) return;
        Controller.SeedPayload = e.Value?.ToString() ?? "";
    }

    private async Task CompileFlow()
    {
        if (Controller is null) return;
        await Controller.CompileCommand.ExecuteAsync(null);
    }

    private async Task RunFlow()
    {
        if (Controller is null) return;
        await Controller.RunCommand.ExecuteAsync(null);
    }

    private async Task StopFlow()
    {
        if (Controller is null) return;
        await Controller.StopCommand.ExecuteAsync(null);
    }

    private async Task CloseFlow()
    {
        if (Controller is null) return;
        await Controller.CloseWorkflowCommand.ExecuteAsync(null);
    }

    public void Dispose()
    {
        if (Controller is INotifyPropertyChanged n)
            n.PropertyChanged -= OnControllerPropertyChanged;
    }
}
