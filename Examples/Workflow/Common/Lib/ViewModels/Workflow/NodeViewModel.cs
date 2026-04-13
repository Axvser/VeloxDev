using Demo.ViewModels.Workflow.Helper;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[WorkflowBuilder.Node             // 构造一个Node组件用于Workflow
    <HttpHelper<NodeViewModel>>   // 逻辑块抽离至自定义的NodeHelper
    (workSemaphore: 5)]           // 该节点执行Work任务时,最多并发5个,超出自动排队
public partial class NodeViewModel
{
    public NodeViewModel() => InitializeWorkflow();

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
    public bool HasWorkLoad => RunCount > 0 || WaitCount > 0;
    public string WorkLoadText => $"Run: {RunCount} · Queue: {WaitCount}";

    [VeloxProperty] public partial IWorkflowSlotViewModel InputSlot { get; set; }
    [VeloxProperty] public partial IWorkflowSlotViewModel OutputSlot { get; set; }

    [VeloxProperty] private string title = "Workflow Step";
    [VeloxProperty] private int delayMilliseconds = 1200;
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

    partial void OnRunCountChanged(int oldValue, int newValue)
    {
        OnPropertyChanged(nameof(HasWorkLoad));
        OnPropertyChanged(nameof(WorkLoadText));
    }

    partial void OnWaitCountChanged(int oldValue, int newValue)
    {
        OnPropertyChanged(nameof(HasWorkLoad));
        OnPropertyChanged(nameof(WorkLoadText));
    }
}