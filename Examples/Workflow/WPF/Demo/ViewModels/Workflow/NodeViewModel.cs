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

    [VeloxProperty] public partial IWorkflowSlotViewModel? InputSlot { get; set; }
    [VeloxProperty] public partial IWorkflowSlotViewModel? OutputSlot { get; set; }

    partial void OnInputSlotChanged(IWorkflowSlotViewModel? oldValue, IWorkflowSlotViewModel? newValue)
    {
        ResetSlot(oldValue, newValue, nameof(HasInputSlot));
    }

    partial void OnOutputSlotChanged(IWorkflowSlotViewModel? oldValue, IWorkflowSlotViewModel? newValue)
    {
        ResetSlot(oldValue, newValue, nameof(HasOutputSlot));
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

    private void ResetSlot(IWorkflowSlotViewModel? oldValue, IWorkflowSlotViewModel? newValue, string stateName)
    {
        if (ReferenceEquals(oldValue, newValue))
        {
            return;
        }

        oldValue?.GetHelper().Delete();
        if (newValue is not null)
        {
            GetHelper().CreateSlot(newValue);
        }

        OnPropertyChanged(stateName);
    }
}