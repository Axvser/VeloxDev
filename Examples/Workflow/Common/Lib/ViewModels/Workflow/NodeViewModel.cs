using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务执行者")]
[WorkflowBuilder.Node
    <HttpHelper<NodeViewModel>>
    (workSemaphore: 5)]
public partial class NodeViewModel
{
    public NodeViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Workflow Step";

    [AgentContext(AgentLanguages.Chinese, "模拟延迟时间")]
    [VeloxProperty] private int delayMilliseconds = 1200;

    [AgentContext(AgentLanguages.Chinese, "是否自动广播给下游节点")]
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