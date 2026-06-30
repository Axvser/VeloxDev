using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务发起者")]
[AgentContext(AgentLanguages.English, "A derived Node component that acts as a workflow initiator/controller. Default size: 300×260. Never use Size(0,0).")]
[WorkflowBuilder.Node<NodeHelper>]
public partial class ControllerViewModel
{
    public ControllerViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "输出口")]
    [AgentContext(AgentLanguages.English, "Output slot (sender). Connect this to the first downstream node's input slot to start the execution chain.")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "是否处于活跃状态")]
    [AgentContext(AgentLanguages.English, "Indicates whether the workflow is currently running.")]
    [VeloxProperty] private bool isActive = false;

    [AgentContext(AgentLanguages.Chinese, "种子负载，工作流执行时的初始数据")]
    [AgentContext(AgentLanguages.English, "Initial payload string injected into the workflow context when execution starts.")]
    [VeloxProperty] private string seedPayload = "demo-request-chain";

    // ── 编译器四维度配置 ────────────────────────────────────────────────

    [AgentContext(AgentLanguages.Chinese, "编译模式：BFS（广度优先）或 DFS（深度优先/前序）")]
    [AgentContext(AgentLanguages.English, "Compile mode: BFS (breadth-first, level by level) or DFS (depth-first, pre-order).")]
    [VeloxProperty] private CompileMode compileMode = CompileMode.BFS;

    [AgentContext(AgentLanguages.Chinese, "边的遍历方向：Forward（顺流而下）或 Reverse（逆流而上）")]
    [AgentContext(AgentLanguages.English, "Edge traversal direction: Forward (follow outputs downstream) or Reverse (follow inputs upstream).")]
    [VeloxProperty] private CompileDirection compileDirection = CompileDirection.Forward;

    [AgentContext(AgentLanguages.Chinese, "遍历范围：FromNode（从当前节点辐射）或 Omni（自动发现全图边界）")]
    [AgentContext(AgentLanguages.English, "Traversal scope: FromNode (start from this node and radiate out) or Omni (auto-discover all graph boundary nodes).")]
    [VeloxProperty] private CompileScope compileScope = CompileScope.FromNode;

    [AgentContext(AgentLanguages.Chinese, "环路处理策略：Throw（抛异常）、Trim（修剪环路）、Allow（保留环路元数据）")]
    [AgentContext(AgentLanguages.English, "Cycle handling: Throw (exception on cycle), Trim (traverse without revisiting), Allow (preserve loop metadata).")]
    [VeloxProperty] private CycleHandling cycleHandling = CycleHandling.Throw;

    // ── ComboBox 数据源（实例属性，避免跨平台 {x:Static} 问题） ──────
    public CompileMode[] CompileModeOptions => [CompileMode.BFS, CompileMode.DFS];
    public CompileDirection[] CompileDirectionOptions => [CompileDirection.Forward, CompileDirection.Reverse];
    public CompileScope[] CompileScopeOptions => [CompileScope.FromNode, CompileScope.Omni];
    public CycleHandling[] CycleHandlingOptions => [CycleHandling.Throw, CycleHandling.Trim, CycleHandling.Allow];

    // WorkResult（生成器 NuGet 暂未生成该属性，手动实现）
    private object? workResult;
    public object? WorkResult
    {
        get => workResult;
        set
        {
            if (Equals(workResult, value)) return;
            workResult = value;
            OnPropertyChanged(nameof(WorkResult));
        }
    }

    // ── ComboBox 数据源（跨平台 XAML 绑定用） ──────────────────────────

    public static CompileMode[] CompileModeValues => [CompileMode.BFS, CompileMode.DFS];
    public static CompileDirection[] CompileDirectionValues => [CompileDirection.Forward, CompileDirection.Reverse];
    public static CompileScope[] CompileScopeValues => [CompileScope.FromNode, CompileScope.Omni];
    public static CycleHandling[] CycleHandlingValues => [CycleHandling.Throw, CycleHandling.Trim, CycleHandling.Allow];

    [AgentContext(AgentLanguages.Chinese, "启动工作流：以自身为起点，按四维度配置编译拓扑后按序执行")]
    [AgentContext(AgentLanguages.English, "Compile the workflow graph with current 4-dimension settings, then execute in deterministic order.")]
    [VeloxCommand]
    private async Task OpenWorkflow(object? parameters, CancellationToken ct)
    {
        var tree = Parent as TreeViewModel;
        tree?.BeginWorkflowRun();

        try
        {
            var compiler = new WorkflowCompiler();
            var context = NetworkFlowContext.Create(SeedPayload);

            // 使用当前四维度配置编译
            var results = compiler.Compile(this, CompileMode, CompileDirection, CompileScope, CycleHandling);
            if (results.Count == 0) return;
            var result = results[0];

            // 按编译顺序执行，结果链自动传递
            await result.ExecuteAsync(context, ct);
        }
        catch
        {
            tree?.EndWorkflowRun();
            throw;
        }
    }

    [AgentContext(AgentLanguages.Chinese, "停止工作流，关闭所有正在运行的任务")]
    [AgentContext(AgentLanguages.English, "Stops the workflow and closes all in-progress work items.")]
    [VeloxCommand]
    private async Task CloseWorkflow(object? parameters, CancellationToken ct)
    {
        if (Parent is null) return;
        await Parent.GetHelper().CloseAsync();
        if (Parent is TreeViewModel tree)
        {
            tree.EndWorkflowRun();
        }
    }
}
