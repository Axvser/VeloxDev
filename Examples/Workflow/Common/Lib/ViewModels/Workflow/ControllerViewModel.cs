using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务发起者")]
[AgentContext(AgentLanguages.English, "A derived Node component that acts as a workflow initiator/controller. Default size: 230×170. Never use Size(0,0).")]
[WorkflowBuilder.Node<NodeHelper>]
[DefaultSize(230, 170)]
public partial class ControllerViewModel : ICompileTimeAware, IRuntimeAware
{
    public ControllerViewModel() => InitializeWorkflow();

    // ── Compile / Run ──────────────────────────────────────────────────────────
    /// <summary>Compilation result (the compiled graphs rooted at this controller).</summary>
    public CompilerViewModel Compiler { get; } = new();

    /// <summary>Current runtime execution session (created on Run; the UI can bind to its progress).</summary>
    public IRuntimeContext? RuntimeContext { get; private set; }

    private CancellationTokenSource? _runCts;

    /// <summary>Whether at least one graph has been compiled (enables the Run button).</summary>
    public bool HasCompiledGraphs => Compiler.Graphs.Count > 0;

    [AgentContext(AgentLanguages.Chinese, "编译：以自身为起点，把可达子图分解成编译图，并给各节点注入编译身份")]
    [AgentContext(AgentLanguages.English, "Compile: decompose the reachable subgraph from this controller into compiled graphs.")]
    [VeloxCommand]
    private async Task Compile(object? parameters, CancellationToken ct)
    {
        await Compiler.CompileAsync(this, CompileRole.Root);
        OnPropertyChanged(nameof(HasCompiledGraphs));
    }

    [AgentContext(AgentLanguages.Chinese, "运行：用编译图 + 执行引擎驱动整条链")]
    [AgentContext(AgentLanguages.English, "Run: drive the compiled graph with the execution engine.")]
    [VeloxCommand]
    private async Task Run(object? parameters, CancellationToken ct)
    {
        var graph = Compiler.Graphs.FirstOrDefault();
        if (graph is null) return;

        // IsActive 是这里写、别处读的：Blazor 的状态栏与 Stop 按钮直接绑它，树的 IsWorkflowRunning 由它推导。
        // 在此之前它从没被写过，所以状态栏永远显示 Idle、Stop 永远禁用。
        var tree = Parent as TreeViewModel;
        IsActive = true;
        tree?.BeginWorkflowRun();

        var context = new RuntimeContext { IsRunning = true };
        RuntimeContext = context;
        OnPropertyChanged(nameof(RuntimeContext));

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            await new RuntimeEngine().RunAsync(graph, context, _runCts.Token);
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;

            // 先清自己的标志再让树重新推导 —— RefreshWorkflowRunningState 读的正是 IsActive，
            // 而同一棵树上可能有多个控制器，只有全部停下才算停
            IsActive = false;
            tree?.RefreshWorkflowRunningState();
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
            IsActive = false;
            tree.RefreshWorkflowRunningState();
        }
    }

    // ── Injected Interfaces ────────────────────────────────────────────────────
    /// <summary>Compile-time identity injected by the compiler (the controller is the origin; Order is usually 0).</summary>
    public ICompileContext? CompileContext { get; private set; }

    public void AttachCompileTimeContext(ICompileContext context)
    {
        CompileContext = context;
        OnPropertyChanged(nameof(CompileContext));
        OnPropertyChanged(nameof(IsCompileStopped));
    }

    /// <summary>Whether the node is in the compile-time absolute stop state.</summary>
    public bool IsCompileStopped => CompileContext is { Order: -1 };

    /// <summary>Runtime injection: hands the current execution session to this node before the engine drives it.</summary>
    public void AttachRuntimeContext(IRuntimeContext context)
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
