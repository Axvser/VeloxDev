using Demo.ViewModels.Workflow.Helper;
using System;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Node
    <NodeHelper>
    (workSemaphore: 5)]
public partial class NodeViewModel
{
    private IWorkflowSlotViewModel? _inputSlot;
    private IWorkflowSlotViewModel? _outputSlot;

    public NodeViewModel()
    {
        InitializeWorkflow();
        BroadcastMode = WorkflowBroadcastMode.BreadthFirst;
        ReverseBroadcastMode = WorkflowBroadcastMode.DepthFirst;
    }

    public Array RequestMethods => Enum.GetValues<NetworkRequestMethod>();
    public Array BroadcastModes => Enum.GetValues<WorkflowBroadcastMode>();
    public Array ReverseBroadcastModes => BroadcastModes;
    public bool HasInputSlot => _inputSlot is not null;
    public bool HasOutputSlot => _outputSlot is not null;
    public bool HasExecutionOrder => LastExecutionOrder > 0;
    public string ExecutionOrderText => LastExecutionOrder > 0 ? $"#{LastExecutionOrder}" : "-";

    public IWorkflowSlotViewModel? InputSlot
    {
        get => _inputSlot;
        set => ResetSlot(ref _inputSlot, value, nameof(InputSlot), nameof(HasInputSlot));
    }

    public IWorkflowSlotViewModel? OutputSlot
    {
        get => _outputSlot;
        set => ResetSlot(ref _outputSlot, value, nameof(OutputSlot), nameof(HasOutputSlot));
    }

    [VeloxProperty] private string title = "HTTP Request";
    [VeloxProperty] private NetworkRequestMethod method = NetworkRequestMethod.Get;
    [VeloxProperty] private string url = string.Empty;
    [VeloxProperty] private string headers = string.Empty;
    [VeloxProperty] private string bodyTemplate = string.Empty;
    [VeloxProperty] private string captureKey = string.Empty;
    [VeloxProperty] private bool autoBroadcast = true;
    [VeloxProperty] private bool isRunning = false;
    [VeloxProperty] private string lastStatus = "Idle";
    [VeloxProperty] private string lastDuration = "-";
    [VeloxProperty] private string lastResponsePreview = "等待执行";
    [VeloxProperty] private string lastError = string.Empty;
    [VeloxProperty] private int lastExecutionOrder = 0;
    [VeloxProperty] private string lastExecutionTrace = "未执行";
    [VeloxProperty] private int runCount = 0;
    [VeloxProperty] private int waitCount = 0;

    partial void OnLastExecutionOrderChanged(int oldValue, int newValue)
    {
        OnPropertyChanged(nameof(HasExecutionOrder));
        OnPropertyChanged(nameof(ExecutionOrderText));
    }

    private void ResetSlot(ref IWorkflowSlotViewModel? field, IWorkflowSlotViewModel? newValue, string propertyName, string stateName)
    {
        if (ReferenceEquals(field, newValue))
        {
            return;
        }

        var oldValue = field;
        OnPropertyChanging(propertyName);
        oldValue?.GetHelper().Delete();
        field = newValue;
        if (newValue is not null)
        {
            GetHelper().CreateSlot(newValue);
        }

        OnPropertyChanged(propertyName);
        OnPropertyChanged(stateName);
    }
}