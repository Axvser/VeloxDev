using Demo.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;
using VeloxDev.WorkflowSystem.Compilation;

namespace Demo.Components.Workflow;

public partial class WorkflowControllerView : ComponentBase, IDisposable
{
    [Parameter]
    public ControllerViewModel? Controller { get; set; }

    private string _seedValue = "";
    private CompileMode _selectedMode = CompileMode.BFS;
    private CompileDirection _selectedDirection = CompileDirection.Forward;
    private CompileScope _selectedScope = CompileScope.FromNode;
    private CycleHandling _selectedCycle = CycleHandling.Throw;

    private readonly CompileMode[] _modeOptions = CompilerConfigOptions.CompileModeValues;
    private readonly CompileDirection[] _directionOptions = CompilerConfigOptions.CompileDirectionValues;
    private readonly CompileScope[] _scopeOptions = CompilerConfigOptions.CompileScopeValues;
    private readonly CycleHandling[] _cycleOptions = CompilerConfigOptions.CycleHandlingValues;

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
        _selectedMode = Controller.CompileMode;
        _selectedDirection = Controller.CompileDirection;
        _selectedScope = Controller.CompileScope;
        _selectedCycle = Controller.CycleHandling;
    }

    private string GetBorderStyle()
        => Controller?.IsActive == true ? "border-color:#67e8f9;" : "border-color:white;";

    private void OnSeedChanged(ChangeEventArgs e)
    {
        if (Controller is null) return;
        Controller.SeedPayload = e.Value?.ToString() ?? "";
    }

    private void OnModeChanged(ChangeEventArgs e)
    {
        if (Controller is null || e.Value is null) return;
        if (Enum.TryParse<CompileMode>(e.Value.ToString(), out var mode))
            Controller.CompileMode = mode;
    }

    private void OnDirectionChanged(ChangeEventArgs e)
    {
        if (Controller is null || e.Value is null) return;
        if (Enum.TryParse<CompileDirection>(e.Value.ToString(), out var dir))
            Controller.CompileDirection = dir;
    }

    private void OnScopeChanged(ChangeEventArgs e)
    {
        if (Controller is null || e.Value is null) return;
        if (Enum.TryParse<CompileScope>(e.Value.ToString(), out var scope))
            Controller.CompileScope = scope;
    }

    private void OnCycleChanged(ChangeEventArgs e)
    {
        if (Controller is null || e.Value is null) return;
        if (Enum.TryParse<CycleHandling>(e.Value.ToString(), out var cycle))
            Controller.CycleHandling = cycle;
    }

    private async Task RunFlow()
    {
        if (Controller is null) return;
        await Controller.OpenWorkflowCommand.ExecuteAsync(null);
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
