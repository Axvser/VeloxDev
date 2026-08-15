using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务发起者")]
[AgentContext(AgentLanguages.English, "A derived Node component that acts as a workflow initiator/controller. Default size: 300×260. Never use Size(0,0).")]
[WorkflowBuilder.Node<NodeHelper>]
public partial class ControllerViewModel : ICompileTimeAware, IRuntimeAware
{
    public ControllerViewModel() => InitializeWorkflow();

    // ── 编译 / 运行 ────────────────────────────────────────────────────────────
    /// <summary>编译结果（以自身为起点的编译图）。</summary>
    public CompilerViewModel Compiler { get; } = new();

    /// <summary>当前运行期的执行会话（Run 时创建，UI 可绑定进度）。</summary>
    public RuntimeContext? RuntimeContext { get; private set; }

    private CancellationTokenSource? _runCts;

    /// <summary>是否已编译出至少一张图（Run 按钮可用）。</summary>
    public bool HasCompiledGraphs => Compiler.Graphs.Count > 0;

    [AgentContext(AgentLanguages.Chinese, "编译：以自身为起点，把可达子图分解成编译图，并给各节点注入编译身份")]
    [AgentContext(AgentLanguages.English, "Compile: decompose the reachable subgraph from this controller into compiled graphs.")]
    [VeloxCommand]
    private async Task Compile(object? parameters, CancellationToken ct)
    {
        await Compiler.CompileAsync(this);
        OnPropertyChanged(nameof(HasCompiledGraphs));
    }

    [AgentContext(AgentLanguages.Chinese, "运行：用编译图 + 执行引擎驱动整条链")]
    [AgentContext(AgentLanguages.English, "Run: drive the compiled graph with the execution engine.")]
    [VeloxCommand]
    private async Task Run(object? parameters, CancellationToken ct)
    {
        var graph = Compiler.Graphs.FirstOrDefault();
        if (graph is null) return;

        var context = new RuntimeContext { IsRunning = true };
        RuntimeContext = context;
        OnPropertyChanged(nameof(RuntimeContext));

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            await new CompilerEngine().RunAsync(graph, context, _runCts.Token);
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
        }
    }

    [AgentContext(AgentLanguages.Chinese, "停止：取消当前运行")]
    [AgentContext(AgentLanguages.English, "Stop: cancel the current run.")]
    [VeloxCommand]
    private Task Stop(object? parameters, CancellationToken ct)
    {
        _runCts?.Cancel();
        return Task.CompletedTask;
    }

    [AgentContext(AgentLanguages.Chinese, "关闭工作流，释放所有正在运行的任务")]
    [AgentContext(AgentLanguages.English, "Closes the workflow and releases all in-progress work items.")]
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

    // ── 注入接口 ───────────────────────────────────────────────────────────────
    /// <summary>编译期注入的编译身份（控制器是起点，Order 通常为 0）。</summary>
    public CompileContext? CompileContext { get; private set; }

    public void AttachCompileTimeContext(CompileContext context)
    {
        CompileContext = context;
        OnPropertyChanged(nameof(CompileContext));
        OnPropertyChanged(nameof(IsCompileStopped));
    }

    /// <summary>编译期是否处于绝对停止状态。</summary>
    public bool IsCompileStopped => CompileContext is { Order: -1 };

    /// <summary>运行期注入：引擎驱动本节点前，把当前执行会话交给它。</summary>
    public void AttachRuntimeContext(RuntimeContext context)
    {
        RuntimeContext = context;
        OnPropertyChanged(nameof(RuntimeContext));
    }

    [AgentContext(AgentLanguages.Chinese, "输出口")]
    [AgentContext(AgentLanguages.English, "Output slot (sender). Connect this to the first downstream node's input slot to start the execution chain.")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "是否处于活跃状态")]
    [AgentContext(AgentLanguages.English, "Indicates whether the workflow is currently running.")]
    [VeloxProperty] private bool isActive = false;

    [AgentContext(AgentLanguages.Chinese, "种子负载，工作流执行时的初始数据")]
    [AgentContext(AgentLanguages.English, "Initial payload string injected into the workflow context when execution starts.")]
    [VeloxProperty] private string seedPayload = "demo-request-chain";
}
