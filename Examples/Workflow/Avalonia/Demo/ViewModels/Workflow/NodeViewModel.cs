using Demo.ViewModels.Workflow.Helper;
using System;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Node // 构造一个Node组件用于Workflow
    <NodeHelper>                // 逻辑块抽离至自定义的NodeHelper
    (workSemaphore: 5)]         // 该节点执行Work任务时,最多并发5个,超出自动排队
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
    public string ChromeBorderBrush => IsRunning ? "#ffd54a" : "#4b4b4b";
    public string ChromeBackground => IsRunning ? "#2d2817" : "#252525";
    public string HeaderBackground => IsRunning ? "#413612" : "#2d2d2d";
    public string HeaderBorderBrush => IsRunning ? "#ffd54a" : "Gray";
    public string ExecutionBackground => IsRunning ? "#2b2415" : "#1f1f1f";
    public string ExecutionBorderBrush => IsRunning ? "#ffd54a" : "Transparent";
    public string DurationForeground => IsRunning ? "#ffd54a" : "#7ec8ff";
    public bool HasExecutionOrder => LastExecutionOrder > 0;
    public string ExecutionOrderText => LastExecutionOrder > 0 ? $"#{LastExecutionOrder}" : "-";

    [VeloxProperty] public partial IWorkflowSlotViewModel InputSlot { get; set; }
    [VeloxProperty] public partial IWorkflowSlotViewModel OutputSlot { get; set; }

    partial void OnInputSlotChanged(IWorkflowSlotViewModel oldValue, IWorkflowSlotViewModel newValue)
    {
        ResetSlot(oldValue, newValue, nameof(HasInputSlot));
    }

    partial void OnOutputSlotChanged(IWorkflowSlotViewModel oldValue, IWorkflowSlotViewModel newValue)
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

    partial void OnIsRunningChanged(bool oldValue, bool newValue)
    {
        OnPropertyChanged(nameof(ChromeBorderBrush));
        OnPropertyChanged(nameof(ChromeBackground));
        OnPropertyChanged(nameof(HeaderBackground));
        OnPropertyChanged(nameof(HeaderBorderBrush));
        OnPropertyChanged(nameof(ExecutionBackground));
        OnPropertyChanged(nameof(ExecutionBorderBrush));
        OnPropertyChanged(nameof(DurationForeground));
    }

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