using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务执行者，默认大小为 320*260 ")]
[AgentContext(AgentLanguages.English, "A derived Node component that acts as a task executor. Default size: 320×260. Never use Size(0,0).")]
[WorkflowBuilder.Node
    <HttpHelper<NodeViewModel>>
    (workSemaphore: 5)]
public partial class NodeViewModel : ICompileTimePriority, ICompileTimeNotifier
{
    public NodeViewModel() => InitializeWorkflow();

    /// <summary>
    /// 编译期回调：节点被编译的瞬间立即获知自己的编译身份。
    /// - 正常编译（选中分支）：Order 与运行时执行顺序一致（BFS items 顺序），
    ///   此处 +1 对齐运行时的 1-based 编号，让下游节点在「点击运行的一瞬」
    ///   即可显示顺序，无需等节点真正开始执行。
    /// - 被略过（未选中分支的独占节点）：item.IsSkipped 为 true，
    ///   不写入流程顺序（LastExecutionOrder 保持 0，徽标不显示），
    ///   但记录 IsCompileSkipped，让节点得知自己「被编译但被略过」。
    /// </summary>
    public void OnCompiled(CompiledItem item)
    {
        IsCompileSkipped = item.IsSkipped;
        LastExecutionOrder = item.IsSkipped ? 0 : item.Order + 1;
    }

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [AgentContext(AgentLanguages.English, "Input slot (receiver). Connect an upstream output slot here to feed data into this node.")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口")]
    [AgentContext(AgentLanguages.English, "Output slot (sender). Connect this to a downstream input slot to forward execution results.")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [AgentContext(AgentLanguages.English, "Display title shown in the node header.")]
    [VeloxProperty] private string title = "Workflow Step";

    [AgentContext(AgentLanguages.Chinese, "模拟延迟时间")]
    [AgentContext(AgentLanguages.English, "Simulated HTTP delay in milliseconds before the node completes its work step.")]
    [VeloxProperty] private int delayMilliseconds = 1200;

    [AgentContext(AgentLanguages.Chinese, "是否自动广播给下游节点")]
    [AgentContext(AgentLanguages.English, "When true, the node automatically forwards the result to all connected downstream nodes after execution.")]
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
    [VeloxProperty] private int compilePriority = 0;

    public int CompilePriority
    {
        get => compilePriority;
        set
        {
            if (compilePriority == value) return;
            compilePriority = value;
            OnPropertyChanged(nameof(CompilePriority));
        }
    }

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

    /// <summary>本次编译中该节点是否因属于未选中条件分支而被略过（编译期判定）。</summary>
    public bool IsCompileSkipped { get; private set; }

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